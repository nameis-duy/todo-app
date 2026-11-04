using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Todo.Api.Endpoints;
using Todo.Api.Middleware;
using Todo.Application.Tasks;
using Todo.Application.Purposes;
using Todo.Infrastructure.Persistence;
using Todo.Api.Authentication;
using Todo.Application.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Todo.Api.Health;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(keysDirectory))
{
	builder.Services.AddDataProtection().SetApplicationName("Todo")
		.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
}
var proxyAddress = builder.Configuration["ReverseProxy:KnownProxy"];
if (!string.IsNullOrWhiteSpace(proxyAddress))
{
	builder.Services.Configure<ForwardedHeadersOptions>(options =>
	{
		options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
		options.KnownProxies.Add(System.Net.IPAddress.Parse(proxyAddress));
		options.ForwardLimit = 1;
	});
}
builder.AddTodoAuthentication();
builder.Services.Configure<TaskStorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddScoped<ITaskGrants, TaskGrants>();
builder.Services.AddScoped<IGuestData, GuestData>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
	options.SerializerOptions.AllowDuplicateProperties = false;
});
builder.Services.AddDbContext<TodoDbContext>(options => options.UseSqlServer(
	builder.Configuration.GetConnectionString("Todo")
		?? throw new InvalidOperationException("Configure ConnectionStrings:Todo before starting the API.")));
if (builder.Configuration.GetValue<bool>("Storage:UseJsonFile"))
{
	var directory = builder.Configuration["Storage:JsonDirectory"];
	if (string.IsNullOrWhiteSpace(directory))
		throw new InvalidOperationException("Configure Storage:JsonDirectory when file storage is enabled.");
	var fullDirectory = Path.GetFullPath(directory, builder.Environment.ContentRootPath);
	builder.Services.AddSingleton(_ => new JsonTaskStore(fullDirectory));
	builder.Services.AddScoped<ITaskRepository, JsonTaskRepository>();
	builder.Services.AddScoped<IPurposeRepository, JsonPurposeRepository>();
}
else
{
	builder.Services.AddScoped<ITaskRepository, TaskRepository>();
	builder.Services.AddScoped<IPurposeRepository, PurposeRepository>();
}
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IPurposeService, PurposeService>();
builder.Services.AddScoped<IValidator<SavePurposeRequest>, SavePurposeValidator>();
builder.Services.AddScoped<IValidator<SaveTaskRequest>, SaveTaskValidator>();
builder.Services.AddScoped<IValidator<TaskQuery>, TaskQueryValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("database", tags: ["ready"]);
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
	if (origins.Length > 0)
		policy.WithOrigins(origins).WithMethods("GET", "POST", "PUT", "PATCH", "DELETE").AllowAnyHeader();
}));

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("ImportJson"))
{
	var directory = builder.Configuration["Storage:JsonDirectory"] ?? "App_Data";
	var fullDirectory = Path.GetFullPath(directory, builder.Environment.ContentRootPath);
	if (!File.Exists(Path.Combine(fullDirectory, "tasks.json")))
		throw new InvalidOperationException("No tasks.json file exists in the configured JSON directory. No data was imported.");
	using var store = new JsonTaskStore(fullDirectory);
	await using var scope = app.Services.CreateAsyncScope();
	var result = await JsonDataImporter.ImportAsync(scope.ServiceProvider.GetRequiredService<TodoDbContext>(), store, CancellationToken.None);
	app.Logger.LogInformation("Imported {Tasks} tasks and {Purposes} purposes. Source JSON was not modified.", result.Tasks, result.Purposes);
	return;
}
app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseTodoAuthentication();
app.MapAuth();
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi().AllowAnonymous();
	app.MapSwaggerUi();
}
app.MapGet("/health/live", () => TypedResults.Ok(new { status = "Healthy" }))
	.AllowAnonymous()
	.WithSummary("Reports process liveness; does not check database connectivity.");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
	Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();
app.MapTasks();
app.MapPurposes();
app.MapFallback("/api/{**path}", () => TypedResults.NotFound()).AllowAnonymous();
app.MapFallback("/health/{**path}", () => TypedResults.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();
app.Run();
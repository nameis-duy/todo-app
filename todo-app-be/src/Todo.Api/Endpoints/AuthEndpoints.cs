using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Todo.Api.Authentication;
using Todo.Application.Security;

namespace Todo.Api.Endpoints;

public sealed record CredentialsRequest(string Email, string Password);
public sealed record AccountResponse(Guid Id, string Email, bool IsGuest = false, bool HasGuestData = false);
public sealed record CsrfTokenResponse(string RequestToken);

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Accounts");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/"
            });
            return TypedResults.NoContent();
        }).AllowAnonymous().WithSummary("Initialize the CSRF token cookie before signing in or sending mutations.");

        auth.MapGet("/csrf-token", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var tokens = antiforgery.GetAndStoreTokens(context);
            return TypedResults.Ok(new CsrfTokenResponse(tokens.RequestToken!));
        }).AllowAnonymous().WithSummary("Get a CSRF request token for native cookie-session clients.");

        auth.MapPost("/guest", async (HttpContext context, GuestSession guests, CancellationToken cancellationToken) =>
        {
            var guest = await guests.StartAsync(context);
            return TypedResults.Ok(await guests.AccountAsync(guest, context, cancellationToken));
        }).AllowAnonymous().RequireRateLimiting("sign-in")
            .WithSummary("Start or resume a private temporary guest session.")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(429).ProducesProblem(503);

        auth.MapPost("/guest-sync", async (HttpContext context, ICurrentUser currentUser, GuestSession guests, CancellationToken cancellationToken) =>
            TypedResults.Ok(await guests.SyncAsync(context, currentUser.Id, cancellationToken)))
            .RequireAuthorization("registered-account")
            .WithSummary("Explicitly merge this browser's guest tasks and projects into the current account.")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(503);

        auth.MapPost("/guest-import", async (OfflineGuestBatch batch, ICurrentUser currentUser, IGuestData data,
            CancellationToken cancellationToken) => TypedResults.Ok(await data.ImportAsync(batch, currentUser.Id, cancellationToken)))
            .RequireAuthorization("registered-account")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(512 * 1024))
            .WithSummary("Import an explicitly approved offline guest batch; repeat the same batch ID and content to retry safely.")
            .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(409).ProducesProblem(413).ProducesProblem(503);

        auth.MapPost("/register", async Task<Results<Created<AccountResponse>, ValidationProblem>> (
            CredentialsRequest request, UserManager<IdentityUser<Guid>> users, SignInManager<IdentityUser<Guid>> signIn,
            HttpContext context, GuestSession guests, CancellationToken cancellationToken) =>
        {
            if (!ValidCredentials(request)) return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                { ["Email"] = ["Enter a valid email (up to 254 characters) and a password (up to 128 characters)."] });
            var email = request.Email.Trim();
            var user = new IdentityUser<Guid> { Id = Guid.NewGuid(), UserName = email, Email = email };
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded) return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                { ["Account"] = ["Registration failed. Use an available email and a password with at least 12 characters, uppercase, lowercase, a number, and a symbol."] });
            await signIn.SignInAsync(user, isPersistent: false);
            return TypedResults.Created("/api/auth/me", await guests.AccountAsync(user, context, cancellationToken));
        }).AllowAnonymous().RequireRateLimiting("sign-in").WithSummary("Register a local account and start a cookie session.");

        auth.MapPost("/login", async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> (
            CredentialsRequest request, UserManager<IdentityUser<Guid>> users, SignInManager<IdentityUser<Guid>> signIn,
            HttpContext context, GuestSession guests, CancellationToken cancellationToken) =>
        {
            if (!ValidCredentials(request)) return TypedResults.Problem(statusCode: 401, title: "Email or password is incorrect.");
            var result = await signIn.PasswordSignInAsync(request.Email.Trim(), request.Password, false, lockoutOnFailure: true);
            if (!result.Succeeded) return TypedResults.Problem(statusCode: 401, title: "Email or password is incorrect.");
            var user = (await users.FindByEmailAsync(request.Email.Trim()))!;
            return TypedResults.Ok(await guests.AccountAsync(user, context, cancellationToken));
        }).AllowAnonymous().RequireRateLimiting("sign-in").WithSummary("Sign in with a local account.");

        auth.MapGet("/me", async (HttpContext context, UserManager<IdentityUser<Guid>> users,
            GuestSession guests, CancellationToken cancellationToken) =>
        {
            var user = await users.GetUserAsync(context.User);
            return user is null ? Results.Unauthorized() : Results.Ok(await guests.AccountAsync(user, context, cancellationToken));
        }).RequireAuthorization().WithSummary("Get the signed-in account.");

        auth.MapPost("/logout", async (SignInManager<IdentityUser<Guid>> signIn) =>
        {
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }).RequireAuthorization().WithSummary("End the current cookie session.");
    }

    private static bool ValidCredentials(CredentialsRequest request) =>
        !string.IsNullOrWhiteSpace(request.Email) && request.Email.Length <= 254
        && new EmailAddressAttribute().IsValid(request.Email.Trim())
        && !string.IsNullOrEmpty(request.Password) && request.Password.Length <= 128;
}
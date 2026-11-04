namespace Todo.Api.Endpoints;

public static class SwaggerUiEndpoints
{
	public static void MapSwaggerUi(this WebApplication app)
	{
		app.MapGet("/swagger", (HttpContext context) =>
			TypedResults.Redirect($"{context.Request.PathBase}/swagger/index.html"))
			.AllowAnonymous()
			.ExcludeFromDescription();
		app.MapGet("/swagger/index.html", () => TypedResults.Content(
			"""
			<!DOCTYPE html>
			<html lang="en">
			<head>
			    <meta charset="utf-8">
			    <meta name="viewport" content="width=device-width, initial-scale=1">
			    <title>Todo API - Swagger UI</title>
			    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5.34.0/swagger-ui.css">
			</head>
			<body>
			    <div id="swagger-ui"></div>
			    <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5.34.0/swagger-ui-bundle.js"></script>
			    <script>
			        SwaggerUIBundle({
			            url: "../openapi/v1.json",
			            dom_id: "#swagger-ui",
			            deepLinking: true,
			            validatorUrl: null,
			            requestInterceptor: async request => {
			                request.credentials = "same-origin";
			                if (!["GET", "HEAD", "OPTIONS"].includes((request.method ?? "GET").toUpperCase())) {
			                    await fetch("../api/auth/csrf", { credentials: "same-origin" });
			                    const token = document.cookie.split("; ").find(value => value.startsWith("XSRF-TOKEN="));
			                    if (token) request.headers["X-XSRF-TOKEN"] = decodeURIComponent(token.slice(11));
			                }
			                return request;
			            }
			        });
			    </script>
			</body>
			</html>
			""", "text/html"))
			.AllowAnonymous()
			.ExcludeFromDescription();
	}
}
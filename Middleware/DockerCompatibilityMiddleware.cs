namespace OciDistributionRegistry.Middleware;

/// <summary>
/// Middleware for adding Docker Registry API headers for compatibility.
/// </summary>
public class DockerCompatibilityMiddleware
{
    private readonly RequestDelegate _next;

    public DockerCompatibilityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add Docker Distribution API version header to all responses
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey("Docker-Distribution-API-Version"))
            {
                context.Response.Headers.Add("Docker-Distribution-API-Version", "registry/2.0");
            }
            return Task.CompletedTask;
        });

        // Set CORS headers for registry operations
        if (context.Request.Method == "OPTIONS")
        {
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, HEAD, PATCH, OPTIONS");
            context.Response.Headers.Add("Access-Control-Allow-Headers", "Authorization, Content-Type, Content-Length, Content-Range, Docker-Content-Digest, Accept");
            context.Response.StatusCode = 200;
            return;
        }

        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Add("Access-Control-Expose-Headers", "Docker-Content-Digest, Location, Range, Content-Length, OCI-Subject, OCI-Filters-Applied");

        await _next(context);
    }
}

/// <summary>
/// Extension method for registering the Docker compatibility middleware.
/// </summary>
public static class DockerCompatibilityMiddlewareExtensions
{
    public static IApplicationBuilder UseDockerCompatibility(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<DockerCompatibilityMiddleware>();
    }
}

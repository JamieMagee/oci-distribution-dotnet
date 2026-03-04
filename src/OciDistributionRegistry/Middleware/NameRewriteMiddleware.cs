using System.Text.RegularExpressions;

namespace OciDistributionRegistry.Middleware;

/// <summary>
/// Rewrites /v2/{multi/segment/name}/blobs/... paths so that the
/// multi-segment repository name is collapsed into a single path segment,
/// allowing ASP.NET Core's {name} route parameter to match.
/// The original name is stored in HttpContext.Items["OciRepositoryName"]
/// and restored in Location headers on the way out.
/// </summary>
public class NameRewriteMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly Regex OciPathPattern = new(
        @"^/v2/(.+?)/(blobs|manifests|tags|referrers)(/.*)?$",
        RegexOptions.Compiled
    );

    // Placeholder that won't collide with real names (OCI names are lowercase + digits)
    private const string Placeholder = "_REPO_";

    public NameRewriteMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (path != null)
        {
            var match = OciPathPattern.Match(path);
            if (match.Success)
            {
                var rawName = match.Groups[1].Value;
                var rest = match.Groups[2].Value + match.Groups[3].Value;
                // Always rewrite to use the placeholder; store real name in Items
                context.Items["OciRepositoryName"] = rawName;
                context.Request.Path = $"/v2/{Placeholder}/{rest}";
            }
        }

        // Restore the original name in Location headers
        context.Response.OnStarting(() =>
        {
            if (
                context.Items.TryGetValue("OciRepositoryName", out var original)
                && original is string originalName
            )
            {
                if (context.Response.Headers.TryGetValue("Location", out var location))
                {
                    context.Response.Headers["Location"] = location
                        .ToString()
                        .Replace(Placeholder, originalName);
                }
            }
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public static class NameRewriteMiddlewareExtensions
{
    public static IApplicationBuilder UseOciNameRewrite(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<NameRewriteMiddleware>();
    }
}

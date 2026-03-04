using Microsoft.AspNetCore.Mvc;
using OciDistributionRegistry.Models;

namespace OciDistributionRegistry.Controllers;

/// <summary>
/// Base API controller for OCI Distribution endpoints.
/// </summary>
[ApiController]
[Route("/v2")]
[Produces("application/json")]
public class DistributionBaseController : ControllerBase
{
    private static readonly System.Text.RegularExpressions.Regex RepositoryNameRegex = new(
        @"^[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*(\/[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*)*$",
        System.Text.RegularExpressions.RegexOptions.Compiled
    );

    protected readonly ILogger<DistributionBaseController> Logger;

    public DistributionBaseController(ILogger<DistributionBaseController> logger)
    {
        Logger = logger;
    }

    /// <summary>
    /// Check that the endpoint implements Docker Registry API V2.
    /// </summary>
    /// <returns>OK if the registry supports the OCI Distribution Specification</returns>
    /// <response code="200">Registry supports OCI Distribution Specification</response>
    /// <response code="401">Authentication required</response>
    /// <response code="404">Registry does not support OCI Distribution Specification</response>
    [HttpGet("")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult CheckApiVersion()
    {
        Logger.LogDebug("API version check requested");

        // Add Docker compatibility headers
        Response.Headers.Add("Docker-Distribution-API-Version", "registry/2.0");

        return Ok();
    }

    /// <summary>
    /// Creates an error response according to OCI spec.
    /// </summary>
    /// <param name="errorCode">The error code</param>
    /// <param name="message">The error message</param>
    /// <param name="detail">Additional error details</param>
    /// <returns>Error response</returns>
    protected ErrorResponse CreateErrorResponse(
        string errorCode,
        string? message = null,
        object? detail = null
    )
    {
        return new ErrorResponse
        {
            Errors = new[]
            {
                new ErrorDetail
                {
                    Code = errorCode,
                    Message = message,
                    Detail = detail,
                },
            },
        };
    }

    /// <summary>
    /// Resolves the real repository name. The name rewrite middleware replaces
    /// multi-segment names with a placeholder for routing; the original is in HttpContext.Items.
    /// </summary>
    protected string ResolveRepositoryName(string routeName)
    {
        if (
            HttpContext.Items.TryGetValue("OciRepositoryName", out var original)
            && original is string realName
        )
        {
            return realName;
        }
        return routeName;
    }

    /// <summary>
    /// Validates repository name and returns appropriate error response if invalid.
    /// Resolves the real repository name from the middleware context.
    /// </summary>
    /// <param name="name">Repository name to validate</param>
    /// <param name="resolvedName">The resolved repository name (from middleware or as-is)</param>
    /// <returns>Null if valid, error response if invalid</returns>
    protected IActionResult? ValidateRepositoryName(string name, out string resolvedName)
    {
        resolvedName = ResolveRepositoryName(name);

        if (string.IsNullOrEmpty(resolvedName))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.NameInvalid, "Repository name cannot be empty")
            );
        }

        if (!RepositoryNameRegex.IsMatch(resolvedName))
        {
            return BadRequest(
                CreateErrorResponse(
                    OciErrorCodes.NameInvalid,
                    "Repository name does not match required format"
                )
            );
        }

        if (resolvedName.Length > 255)
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.NameInvalid, "Repository name too long")
            );
        }

        return null;
    }

    /// <summary>
    /// Adds Docker compatibility headers to the response.
    /// </summary>
    /// <param name="digest">Content digest</param>
    protected void AddDockerHeaders(string? digest = null)
    {
        Response.Headers.Add("Docker-Distribution-API-Version", "registry/2.0");
        if (!string.IsNullOrEmpty(digest))
        {
            Response.Headers.Add("Docker-Content-Digest", digest);
        }
    }
}

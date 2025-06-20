using System.Text.Json;
using OciDistributionRegistry.Models;

namespace OciDistributionRegistry.Middleware;

/// <summary>
/// Middleware for handling exceptions and converting them to OCI-compliant error responses.
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var errorResponse = exception switch
        {
            ArgumentException => new ErrorResponse
            {
                Errors = new[]
                {
                    new ErrorDetail
                    {
                        Code = OciErrorCodes.NameInvalid,
                        Message = exception.Message
                    }
                }
            },
            UnauthorizedAccessException => new ErrorResponse
            {
                Errors = new[]
                {
                    new ErrorDetail
                    {
                        Code = OciErrorCodes.Unauthorized,
                        Message = "Authentication required"
                    }
                }
            },
            FileNotFoundException => new ErrorResponse
            {
                Errors = new[]
                {
                    new ErrorDetail
                    {
                        Code = OciErrorCodes.BlobUnknown,
                        Message = "Content not found"
                    }
                }
            },
            NotSupportedException => new ErrorResponse
            {
                Errors = new[]
                {
                    new ErrorDetail
                    {
                        Code = OciErrorCodes.Unsupported,
                        Message = exception.Message
                    }
                }
            },
            _ => new ErrorResponse
            {
                Errors = new[]
                {
                    new ErrorDetail
                    {
                        Code = "UNKNOWN",
                        Message = "An error occurred processing the request"
                    }
                }
            }
        };

        response.StatusCode = exception switch
        {
            ArgumentException => 400,
            UnauthorizedAccessException => 401,
            FileNotFoundException => 404,
            NotSupportedException => 405,
            _ => 500
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse);
        await response.WriteAsync(jsonResponse);
    }
}

/// <summary>
/// Extension method for registering the error handling middleware.
/// </summary>
public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseOciErrorHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ErrorHandlingMiddleware>();
    }
}

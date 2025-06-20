using System.Text.Json.Serialization;

namespace OciDistributionRegistry.Models;

/// <summary>
/// Represents the response for listing tags.
/// </summary>
public class TagList
{
    /// <summary>
    /// The repository name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// The list of tags.
    /// </summary>
    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }
}

/// <summary>
/// Represents an error response.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// The list of errors.
    /// </summary>
    [JsonPropertyName("errors")]
    public required ErrorDetail[] Errors { get; set; }
}

/// <summary>
/// Represents an individual error detail.
/// </summary>
public class ErrorDetail
{
    /// <summary>
    /// The error code.
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; set; }

    /// <summary>
    /// The error message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Additional error details.
    /// </summary>
    [JsonPropertyName("detail")]
    public object? Detail { get; set; }
}

using System.Text.Json.Serialization;

namespace OciDistributionRegistry.Models;

/// <summary>
/// Represents a content descriptor as defined in the OCI Image Specification.
/// </summary>
public class Descriptor
{
    /// <summary>
    /// The media type of the referenced content.
    /// </summary>
    [JsonPropertyName("mediaType")]
    public required string MediaType { get; set; }

    /// <summary>
    /// The digest of the referenced content.
    /// </summary>
    [JsonPropertyName("digest")]
    public required string Digest { get; set; }

    /// <summary>
    /// The size in bytes of the referenced content.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Optional URLs to the referenced content.
    /// </summary>
    [JsonPropertyName("urls")]
    public string[]? Urls { get; set; }

    /// <summary>
    /// Optional annotations for the descriptor.
    /// </summary>
    [JsonPropertyName("annotations")]
    public Dictionary<string, string>? Annotations { get; set; }

    /// <summary>
    /// Optional data for the descriptor.
    /// </summary>
    [JsonPropertyName("data")]
    public byte[]? Data { get; set; }

    /// <summary>
    /// Optional artifact type for the descriptor.
    /// </summary>
    [JsonPropertyName("artifactType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactType { get; set; }
}

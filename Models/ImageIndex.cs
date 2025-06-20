using System.Text.Json.Serialization;

namespace OciDistributionRegistry.Models;

/// <summary>
/// Represents an OCI Image Index as defined in the OCI Image Specification.
/// </summary>
public class ImageIndex
{
    /// <summary>
    /// The schema version of the index format.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    /// <summary>
    /// The media type of the index.
    /// </summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>
    /// The artifact type of the index (optional).
    /// </summary>
    [JsonPropertyName("artifactType")]
    public string? ArtifactType { get; set; }

    /// <summary>
    /// The list of manifest descriptors.
    /// </summary>
    [JsonPropertyName("manifests")]
    public required Descriptor[] Manifests { get; set; }

    /// <summary>
    /// The subject descriptor (optional).
    /// </summary>
    [JsonPropertyName("subject")]
    public Descriptor? Subject { get; set; }

    /// <summary>
    /// Optional annotations for the index.
    /// </summary>
    [JsonPropertyName("annotations")]
    public Dictionary<string, string>? Annotations { get; set; }
}

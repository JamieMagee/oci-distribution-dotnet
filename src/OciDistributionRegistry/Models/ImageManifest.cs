using System.Text.Json.Serialization;

namespace OciDistributionRegistry.Models;

/// <summary>
/// Represents an OCI Image Manifest as defined in the OCI Image Specification.
/// </summary>
public class ImageManifest
{
    /// <summary>
    /// The schema version of the manifest format.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    /// <summary>
    /// The media type of the manifest.
    /// </summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>
    /// The artifact type of the manifest (optional).
    /// </summary>
    [JsonPropertyName("artifactType")]
    public string? ArtifactType { get; set; }

    /// <summary>
    /// The configuration descriptor.
    /// </summary>
    [JsonPropertyName("config")]
    public required Descriptor Config { get; set; }

    /// <summary>
    /// The list of layer descriptors.
    /// </summary>
    [JsonPropertyName("layers")]
    public required Descriptor[] Layers { get; set; }

    /// <summary>
    /// The subject descriptor (optional).
    /// </summary>
    [JsonPropertyName("subject")]
    public Descriptor? Subject { get; set; }

    /// <summary>
    /// Optional annotations for the manifest.
    /// </summary>
    [JsonPropertyName("annotations")]
    public Dictionary<string, string>? Annotations { get; set; }
}

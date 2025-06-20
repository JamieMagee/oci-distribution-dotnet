namespace OciDistributionRegistry.Models;

/// <summary>
/// OCI Distribution Specification error codes.
/// </summary>
public static class OciErrorCodes
{
    public const string BlobUnknown = "BLOB_UNKNOWN";
    public const string BlobUploadInvalid = "BLOB_UPLOAD_INVALID";
    public const string BlobUploadUnknown = "BLOB_UPLOAD_UNKNOWN";
    public const string DigestInvalid = "DIGEST_INVALID";
    public const string ManifestBlobUnknown = "MANIFEST_BLOB_UNKNOWN";
    public const string ManifestInvalid = "MANIFEST_INVALID";
    public const string ManifestUnknown = "MANIFEST_UNKNOWN";
    public const string NameInvalid = "NAME_INVALID";
    public const string NameUnknown = "NAME_UNKNOWN";
    public const string SizeInvalid = "SIZE_INVALID";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Denied = "DENIED";
    public const string Unsupported = "UNSUPPORTED";
    public const string TooManyRequests = "TOOMANYREQUESTS";
}

/// <summary>
/// OCI media types.
/// </summary>
public static class OciMediaTypes
{
    public const string ImageManifest = "application/vnd.oci.image.manifest.v1+json";
    public const string ImageIndex = "application/vnd.oci.image.index.v1+json";
    public const string ImageConfig = "application/vnd.oci.image.config.v1+json";
    public const string ImageLayer = "application/vnd.oci.image.layer.v1.tar";
    public const string ImageLayerGzip = "application/vnd.oci.image.layer.v1.tar+gzip";
    public const string ImageLayerZstd = "application/vnd.oci.image.layer.v1.tar+zstd";
    public const string EmptyJSON = "application/vnd.oci.empty.v1+json";
    
    // Docker compatibility
    public const string DockerManifest = "application/vnd.docker.distribution.manifest.v2+json";
    public const string DockerManifestList = "application/vnd.docker.distribution.manifest.list.v2+json";
}

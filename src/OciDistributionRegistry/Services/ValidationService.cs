using System.Security.Cryptography;
using System.Text.Json;
using OciDistributionRegistry.Models;
using OciDistributionRegistry.Repositories;

namespace OciDistributionRegistry.Services;

/// <summary>
/// Service for validating OCI distribution requests and content.
/// </summary>
public interface IValidationService
{
    /// <summary>
    /// Validates a repository name according to OCI spec.
    /// </summary>
    /// <param name="name">The repository name</param>
    /// <returns>True if valid, false otherwise</returns>
    bool IsValidRepositoryName(string name);

    /// <summary>
    /// Validates a tag name according to OCI spec.
    /// </summary>
    /// <param name="tag">The tag name</param>
    /// <returns>True if valid, false otherwise</returns>
    bool IsValidTag(string tag);

    /// <summary>
    /// Validates a digest format according to OCI spec.
    /// </summary>
    /// <param name="digest">The digest</param>
    /// <returns>True if valid, false otherwise</returns>
    bool IsValidDigest(string digest);

    /// <summary>
    /// Validates a manifest according to OCI spec.
    /// </summary>
    /// <param name="manifestData">The manifest data</param>
    /// <param name="mediaType">The manifest media type</param>
    /// <returns>Validation result</returns>
    Task<ValidationResult> ValidateManifestAsync(byte[] manifestData, string mediaType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a content range header.
    /// </summary>
    /// <param name="contentRange">The content range header value</param>
    /// <param name="contentLength">The content length</param>
    /// <returns>Validation result with parsed range</returns>
    ValidationResult<(long Start, long End)> ValidateContentRange(string contentRange, long contentLength);

    /// <summary>
    /// Computes the digest of data.
    /// </summary>
    /// <param name="data">The data</param>
    /// <param name="algorithm">The hash algorithm (default: sha256)</param>
    /// <returns>The digest string</returns>
    string ComputeDigest(byte[] data, string algorithm = "sha256");

    /// <summary>
    /// Verifies that data matches the expected digest.
    /// </summary>
    /// <param name="data">The data</param>
    /// <param name="expectedDigest">The expected digest</param>
    /// <returns>True if match, false otherwise</returns>
    bool VerifyDigest(byte[] data, string expectedDigest);
}

/// <summary>
/// Validation result.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }

    public static ValidationResult Success() => new() { IsValid = true };
    public static ValidationResult Failure(string errorMessage, string? errorCode = null) => 
        new() { IsValid = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
}

/// <summary>
/// Validation result with a value.
/// </summary>
public class ValidationResult<T> : ValidationResult
{
    public T? Value { get; set; }

    public static ValidationResult<T> Success(T value) => new() { IsValid = true, Value = value };
    public static new ValidationResult<T> Failure(string errorMessage, string? errorCode = null) => 
        new() { IsValid = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
}

/// <summary>
/// Implementation of the validation service.
/// </summary>
public class ValidationService : IValidationService
{
    private readonly IBlobRepository _blobRepository;
    private readonly ILogger<ValidationService> _logger;

    // OCI spec regular expressions
    private static readonly System.Text.RegularExpressions.Regex RepositoryNameRegex = 
        new(@"^[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*(\/[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*)*$", 
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex TagRegex = 
        new(@"^[a-zA-Z0-9_][a-zA-Z0-9._-]{0,127}$", 
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex DigestRegex = 
        new(@"^[a-z0-9]+([+._-][a-z0-9]+)*:[a-fA-F0-9]+$", 
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ContentRangeRegex = 
        new(@"^(\d+)-(\d+)$", 
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public ValidationService(IBlobRepository blobRepository, ILogger<ValidationService> logger)
    {
        _blobRepository = blobRepository;
        _logger = logger;
    }

    public bool IsValidRepositoryName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255)
            return false;

        return RepositoryNameRegex.IsMatch(name);
    }

    public bool IsValidTag(string tag)
    {
        if (string.IsNullOrEmpty(tag) || tag.Length > 128)
            return false;

        return TagRegex.IsMatch(tag);
    }

    public bool IsValidDigest(string digest)
    {
        if (string.IsNullOrEmpty(digest))
            return false;

        return DigestRegex.IsMatch(digest);
    }

    public async Task<ValidationResult> ValidateManifestAsync(byte[] manifestData, string mediaType, CancellationToken cancellationToken = default)
    {
        try
        {
            // Parse JSON
            using var doc = JsonDocument.Parse(manifestData);
            var root = doc.RootElement;

            // Validate schema version
            if (!root.TryGetProperty("schemaVersion", out var schemaVersionElement) || 
                schemaVersionElement.GetInt32() != 2)
            {
                return ValidationResult.Failure("Invalid or missing schemaVersion", OciErrorCodes.ManifestInvalid);
            }

            // Validate media type if present
            if (root.TryGetProperty("mediaType", out var mediaTypeElement))
            {
                var manifestMediaType = mediaTypeElement.GetString();
                if (manifestMediaType != mediaType)
                {
                    return ValidationResult.Failure("MediaType mismatch", OciErrorCodes.ManifestInvalid);
                }
            }

            // Validate based on manifest type
            if (mediaType == OciMediaTypes.ImageManifest || mediaType == OciMediaTypes.DockerManifest)
            {
                return await ValidateImageManifestAsync(root, cancellationToken);
            }
            else if (mediaType == OciMediaTypes.ImageIndex || mediaType == OciMediaTypes.DockerManifestList)
            {
                return await ValidateImageIndexAsync(root, cancellationToken);
            }

            return ValidationResult.Success();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in manifest");
            return ValidationResult.Failure("Invalid JSON in manifest", OciErrorCodes.ManifestInvalid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating manifest");
            return ValidationResult.Failure("Internal validation error", OciErrorCodes.ManifestInvalid);
        }
    }

    public ValidationResult<(long Start, long End)> ValidateContentRange(string contentRange, long contentLength)
    {
        if (string.IsNullOrEmpty(contentRange))
        {
            return ValidationResult<(long Start, long End)>.Failure("Content-Range header is required", OciErrorCodes.SizeInvalid);
        }

        var match = ContentRangeRegex.Match(contentRange);
        if (!match.Success)
        {
            return ValidationResult<(long Start, long End)>.Failure("Invalid Content-Range format", OciErrorCodes.SizeInvalid);
        }

        if (!long.TryParse(match.Groups[1].Value, out var start) ||
            !long.TryParse(match.Groups[2].Value, out var end))
        {
            return ValidationResult<(long Start, long End)>.Failure("Invalid range values", OciErrorCodes.SizeInvalid);
        }

        if (start > end)
        {
            return ValidationResult<(long Start, long End)>.Failure("Invalid range: start > end", OciErrorCodes.SizeInvalid);
        }

        var expectedLength = end - start + 1;
        if (expectedLength != contentLength)
        {
            return ValidationResult<(long Start, long End)>.Failure("Content-Length does not match range", OciErrorCodes.SizeInvalid);
        }

        return ValidationResult<(long Start, long End)>.Success((start, end));
    }

    public string ComputeDigest(byte[] data, string algorithm = "sha256")
    {
        return algorithm.ToLowerInvariant() switch
        {
            "sha256" => ComputeSha256Digest(data),
            "sha512" => ComputeSha512Digest(data),
            _ => throw new ArgumentException($"Unsupported hash algorithm: {algorithm}")
        };
    }

    public bool VerifyDigest(byte[] data, string expectedDigest)
    {
        var parts = expectedDigest.Split(':', 2);
        if (parts.Length != 2)
            return false;

        var algorithm = parts[0];
        var actualDigest = ComputeDigest(data, algorithm);
        return string.Equals(actualDigest, expectedDigest, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ValidationResult> ValidateImageManifestAsync(JsonElement root, CancellationToken cancellationToken)
    {
        // Validate config descriptor
        if (!root.TryGetProperty("config", out var configElement))
        {
            return ValidationResult.Failure("Missing config descriptor", OciErrorCodes.ManifestInvalid);
        }

        var configValidation = ValidateDescriptor(configElement);
        if (!configValidation.IsValid)
            return configValidation;

        // Validate layers array
        if (!root.TryGetProperty("layers", out var layersElement) || layersElement.ValueKind != JsonValueKind.Array)
        {
            return ValidationResult.Failure("Missing or invalid layers array", OciErrorCodes.ManifestInvalid);
        }

        foreach (var layer in layersElement.EnumerateArray())
        {
            var layerValidation = ValidateDescriptor(layer);
            if (!layerValidation.IsValid)
                return layerValidation;
        }

        // Validate subject descriptor if present
        if (root.TryGetProperty("subject", out var subjectElement))
        {
            var subjectValidation = ValidateDescriptor(subjectElement);
            if (!subjectValidation.IsValid)
                return subjectValidation;
        }

        return ValidationResult.Success();
    }

    private async Task<ValidationResult> ValidateImageIndexAsync(JsonElement root, CancellationToken cancellationToken)
    {
        // Validate manifests array
        if (!root.TryGetProperty("manifests", out var manifestsElement) || manifestsElement.ValueKind != JsonValueKind.Array)
        {
            return ValidationResult.Failure("Missing or invalid manifests array", OciErrorCodes.ManifestInvalid);
        }

        foreach (var manifest in manifestsElement.EnumerateArray())
        {
            var manifestValidation = ValidateDescriptor(manifest);
            if (!manifestValidation.IsValid)
                return manifestValidation;
        }

        // Validate subject descriptor if present
        if (root.TryGetProperty("subject", out var subjectElement))
        {
            var subjectValidation = ValidateDescriptor(subjectElement);
            if (!subjectValidation.IsValid)
                return subjectValidation;
        }

        return ValidationResult.Success();
    }

    private ValidationResult ValidateDescriptor(JsonElement descriptor)
    {
        // Validate required fields
        if (!descriptor.TryGetProperty("mediaType", out var mediaTypeElement) ||
            string.IsNullOrEmpty(mediaTypeElement.GetString()))
        {
            return ValidationResult.Failure("Descriptor missing mediaType", OciErrorCodes.ManifestInvalid);
        }

        if (!descriptor.TryGetProperty("digest", out var digestElement))
        {
            return ValidationResult.Failure("Descriptor missing digest", OciErrorCodes.ManifestInvalid);
        }

        var digest = digestElement.GetString();
        if (string.IsNullOrEmpty(digest) || !IsValidDigest(digest))
        {
            return ValidationResult.Failure("Descriptor has invalid digest", OciErrorCodes.DigestInvalid);
        }

        if (!descriptor.TryGetProperty("size", out var sizeElement) || sizeElement.GetInt64() < 0)
        {
            return ValidationResult.Failure("Descriptor missing or invalid size", OciErrorCodes.ManifestInvalid);
        }

        return ValidationResult.Success();
    }

    private string ComputeSha256Digest(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return $"sha256:{hashString}";
    }

    private string ComputeSha512Digest(byte[] data)
    {
        using var sha512 = SHA512.Create();
        var hashBytes = sha512.ComputeHash(data);
        var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return $"sha512:{hashString}";
    }
}

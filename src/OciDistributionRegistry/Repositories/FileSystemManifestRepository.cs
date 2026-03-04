using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OciDistributionRegistry.Models;

namespace OciDistributionRegistry.Repositories;

/// <summary>
/// File system-based implementation of the manifest repository.
/// </summary>
public class FileSystemManifestRepository : IManifestRepository
{
    private readonly string _repositoriesPath;
    private readonly ILogger<FileSystemManifestRepository> _logger;

    public FileSystemManifestRepository(IConfiguration configuration, ILogger<FileSystemManifestRepository> logger)
    {
        _logger = logger;
        var storagePath = configuration.GetValue<string>("Storage:Path") ?? "/tmp/oci-registry";
        _repositoriesPath = Path.Combine(storagePath, "repositories");
        Directory.CreateDirectory(_repositoriesPath);
    }

    public async Task<bool> ExistsAsync(string repository, string reference, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestPath(repository, reference);
        return File.Exists(manifestPath);
    }

    public async Task<(byte[] Data, string MediaType, string Digest)?> GetAsync(string repository, string reference, string[]? acceptTypes = null, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestPath(repository, reference);
        if (!File.Exists(manifestPath))
            return null;

        var data = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var digest = ComputeDigest(data);
        
        // Try to read persisted media type from sidecar, fall back to detection
        var mediaTypePath = manifestPath + ".mediatype";
        var mediaType = File.Exists(mediaTypePath) 
            ? (await File.ReadAllTextAsync(mediaTypePath, cancellationToken)).Trim()
            : DetermineMediaType(data);
        
        // Check if the media type is acceptable
        if (acceptTypes != null && acceptTypes.Length > 0 && !acceptTypes.Contains(mediaType))
        {
            // For simplicity, return the first acceptable type if conversion is possible
            // In a real implementation, you might convert between formats
            if (acceptTypes.Contains(OciMediaTypes.ImageManifest) || acceptTypes.Contains(OciMediaTypes.ImageIndex))
                mediaType = acceptTypes.First();
        }

        return (data, mediaType, digest);
    }

    public async Task<string> StoreAsync(string repository, string reference, byte[] data, string mediaType, CancellationToken cancellationToken = default)
    {
        var digest = ComputeDigest(data);
        
        // Store by digest
        var digestPath = GetManifestPath(repository, digest);
        var digestDirectory = Path.GetDirectoryName(digestPath)!;
        Directory.CreateDirectory(digestDirectory);
        await File.WriteAllBytesAsync(digestPath, data, cancellationToken);
        await File.WriteAllTextAsync(digestPath + ".mediatype", mediaType, cancellationToken);

        // If reference is a tag, create a symlink or copy
        if (!IsDigest(reference))
        {
            var tagPath = GetManifestPath(repository, reference);
            var tagDirectory = Path.GetDirectoryName(tagPath)!;
            Directory.CreateDirectory(tagDirectory);
            await File.WriteAllBytesAsync(tagPath, data, cancellationToken);
            await File.WriteAllTextAsync(tagPath + ".mediatype", mediaType, cancellationToken);
            
            // Store tag mapping
            await StoreTagMappingAsync(repository, reference, digest, cancellationToken);
        }

        // Handle subject relationships for referrers
        await HandleSubjectRelationshipAsync(repository, data, digest, mediaType, cancellationToken);

        _logger.LogInformation("Stored manifest {Reference} in repository {Repository} with digest {Digest}", reference, repository, digest);
        return digest;
    }

    public async Task<bool> DeleteAsync(string repository, string reference, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestPath(repository, reference);
        if (!File.Exists(manifestPath))
            return false;

        // Get digest before deletion for referrer cleanup
        var data = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var digest = ComputeDigest(data);

        File.Delete(manifestPath);

        var mediaTypeSidecar = manifestPath + ".mediatype";
        if (File.Exists(mediaTypeSidecar))
            File.Delete(mediaTypeSidecar);

        // If deleting by tag, also remove tag mapping
        if (!IsDigest(reference))
        {
            await RemoveTagMappingAsync(repository, reference, cancellationToken);
        }

        // If deleting by digest, invalidate tags pointing to this digest
        if (IsDigest(reference))
        {
            await InvalidateTagsForDigestAsync(repository, reference, cancellationToken);
        }

        // Clean up referrer relationships
        await CleanupReferrerRelationshipsAsync(repository, digest, cancellationToken);

        _logger.LogInformation("Deleted manifest {Reference} from repository {Repository}", reference, repository);
        return true;
    }

    public async Task<(string[] Tags, string? NextTag)> GetTagsAsync(string repository, int? n = null, string? last = null, CancellationToken cancellationToken = default)
    {
        var tagsPath = GetTagsPath(repository);
        if (!File.Exists(tagsPath))
            return (Array.Empty<string>(), null);

        var allTags = (await File.ReadAllTextAsync(tagsPath, cancellationToken))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        var startIndex = 0;
        if (!string.IsNullOrEmpty(last))
        {
            startIndex = Array.BinarySearch(allTags, last, StringComparer.Ordinal);
            if (startIndex < 0)
                startIndex = ~startIndex;
            else
                startIndex++; // Skip the 'last' tag itself
        }

        var count = n ?? allTags.Length - startIndex;
        var tags = allTags.Skip(startIndex).Take(count).ToArray();
        var nextTag = (startIndex + tags.Length < allTags.Length) ? allTags[startIndex + tags.Length] : null;

        return (tags, nextTag);
    }

    public async Task<byte[]> GetReferrersAsync(string repository, string digest, string? artifactType = null, CancellationToken cancellationToken = default)
    {
        var referrersPath = GetReferrersPath(repository, digest);
        
        if (File.Exists(referrersPath))
        {
            var referrersData = await File.ReadAllBytesAsync(referrersPath, cancellationToken);
            
            // Apply artifact type filter if specified
            if (!string.IsNullOrEmpty(artifactType))
            {
                var index = JsonSerializer.Deserialize<ImageIndex>(referrersData);
                if (index != null)
                {
                    var filteredManifests = index.Manifests
                        .Where(m => m.ArtifactType == artifactType)
                        .ToArray();
                    
                    index.Manifests = filteredManifests;
                    return JsonSerializer.SerializeToUtf8Bytes(index);
                }
            }
            
            return referrersData;
        }

        // Return empty index
        var emptyIndex = new ImageIndex
        {
            SchemaVersion = 2,
            MediaType = OciMediaTypes.ImageIndex,
            Manifests = Array.Empty<Descriptor>()
        };

        return JsonSerializer.SerializeToUtf8Bytes(emptyIndex);
    }

    public async Task AddReferrerAsync(string repository, string subjectDigest, string referrerDigest, string artifactType, string mediaType, Dictionary<string, string>? annotations = null, CancellationToken cancellationToken = default)
    {
        var referrersPath = GetReferrersPath(repository, subjectDigest);
        var referrersDir = Path.GetDirectoryName(referrersPath)!;
        Directory.CreateDirectory(referrersDir);

        ImageIndex index;
        if (File.Exists(referrersPath))
        {
            var existingData = await File.ReadAllBytesAsync(referrersPath, cancellationToken);
            index = JsonSerializer.Deserialize<ImageIndex>(existingData) ?? new ImageIndex { Manifests = Array.Empty<Descriptor>() };
        }
        else
        {
            index = new ImageIndex
            {
                SchemaVersion = 2,
                MediaType = OciMediaTypes.ImageIndex,
                Manifests = Array.Empty<Descriptor>()
            };
        }

        // Check if referrer already exists
        if (index.Manifests.Any(m => m.Digest == referrerDigest))
            return;

        // Get referrer manifest size
        var referrerPath = GetManifestPath(repository, referrerDigest);
        var size = new FileInfo(referrerPath).Length;

        var descriptor = new Descriptor
        {
            MediaType = mediaType,
            Digest = referrerDigest,
            Size = size,
            ArtifactType = artifactType,
            Annotations = annotations
        };

        var manifestsList = index.Manifests.ToList();
        manifestsList.Add(descriptor);
        index.Manifests = manifestsList.ToArray();

        var indexData = JsonSerializer.SerializeToUtf8Bytes(index);
        await File.WriteAllBytesAsync(referrersPath, indexData, cancellationToken);

        _logger.LogDebug("Added referrer {ReferrerDigest} to subject {SubjectDigest} in repository {Repository}", referrerDigest, subjectDigest, repository);
    }

    public async Task RemoveReferrerAsync(string repository, string subjectDigest, string referrerDigest, CancellationToken cancellationToken = default)
    {
        var referrersPath = GetReferrersPath(repository, subjectDigest);
        if (!File.Exists(referrersPath))
            return;

        var existingData = await File.ReadAllBytesAsync(referrersPath, cancellationToken);
        var index = JsonSerializer.Deserialize<ImageIndex>(existingData);
        if (index == null)
            return;

        var updatedManifests = index.Manifests.Where(m => m.Digest != referrerDigest).ToArray();
        index.Manifests = updatedManifests;

        if (updatedManifests.Length == 0)
        {
            File.Delete(referrersPath);
        }
        else
        {
            var indexData = JsonSerializer.SerializeToUtf8Bytes(index);
            await File.WriteAllBytesAsync(referrersPath, indexData, cancellationToken);
        }

        _logger.LogDebug("Removed referrer {ReferrerDigest} from subject {SubjectDigest} in repository {Repository}", referrerDigest, subjectDigest, repository);
    }

    private string GetManifestPath(string repository, string reference)
    {
        var repoPath = Path.Combine(_repositoriesPath, repository);
        return Path.Combine(repoPath, "manifests", reference);
    }

    private string GetTagsPath(string repository)
    {
        var repoPath = Path.Combine(_repositoriesPath, repository);
        return Path.Combine(repoPath, "tags.txt");
    }

    private string GetReferrersPath(string repository, string digest)
    {
        var repoPath = Path.Combine(_repositoriesPath, repository);
        return Path.Combine(repoPath, "referrers", digest);
    }

    private async Task StoreTagMappingAsync(string repository, string tag, string digest, CancellationToken cancellationToken)
    {
        var tagsPath = GetTagsPath(repository);
        var tagsDir = Path.GetDirectoryName(tagsPath)!;
        Directory.CreateDirectory(tagsDir);

        var existingTags = new HashSet<string>();
        if (File.Exists(tagsPath))
        {
            var content = await File.ReadAllTextAsync(tagsPath, cancellationToken);
            existingTags = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        }

        existingTags.Add(tag);
        var sortedTags = existingTags.OrderBy(t => t, StringComparer.Ordinal);
        await File.WriteAllTextAsync(tagsPath, string.Join('\n', sortedTags), cancellationToken);
    }

    private async Task RemoveTagMappingAsync(string repository, string tag, CancellationToken cancellationToken)
    {
        var tagsPath = GetTagsPath(repository);
        if (!File.Exists(tagsPath))
            return;

        var content = await File.ReadAllTextAsync(tagsPath, cancellationToken);
        var tags = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t != tag)
            .OrderBy(t => t, StringComparer.Ordinal);

        await File.WriteAllTextAsync(tagsPath, string.Join('\n', tags), cancellationToken);
    }

    private async Task InvalidateTagsForDigestAsync(string repository, string digest, CancellationToken cancellationToken)
    {
        var tagsPath = GetTagsPath(repository);
        if (!File.Exists(tagsPath))
            return;

        var content = await File.ReadAllTextAsync(tagsPath, cancellationToken);
        var tags = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var tag in tags)
        {
            var tagManifestPath = GetManifestPath(repository, tag);
            if (!File.Exists(tagManifestPath))
                continue;

            var tagData = await File.ReadAllBytesAsync(tagManifestPath, cancellationToken);
            var tagDigest = ComputeDigest(tagData);
            if (tagDigest == digest)
            {
                File.Delete(tagManifestPath);
                var tagMediaTypePath = tagManifestPath + ".mediatype";
                if (File.Exists(tagMediaTypePath))
                    File.Delete(tagMediaTypePath);
            }
        }

        // Rebuild tag list removing deleted tags
        var remainingTags = new List<string>();
        var allTags = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var tag in allTags)
        {
            var tagManifestPath = GetManifestPath(repository, tag);
            if (File.Exists(tagManifestPath))
                remainingTags.Add(tag);
        }

        var sorted = remainingTags.OrderBy(t => t, StringComparer.Ordinal);
        await File.WriteAllTextAsync(tagsPath, string.Join('\n', sorted), cancellationToken);
    }

    private async Task HandleSubjectRelationshipAsync(string repository, byte[] manifestData, string manifestDigest, string mediaType, CancellationToken cancellationToken)
    {
        try
        {
            // Check if manifest has subject field
            using var doc = JsonDocument.Parse(manifestData);
            if (doc.RootElement.TryGetProperty("subject", out var subjectElement))
            {
                var subjectDigest = subjectElement.GetProperty("digest").GetString();
                if (!string.IsNullOrEmpty(subjectDigest))
                {
                    // Determine artifact type
                    var artifactType = "";
                    if (doc.RootElement.TryGetProperty("artifactType", out var artifactTypeElement))
                    {
                        artifactType = artifactTypeElement.GetString() ?? "";
                    }
                    else if (mediaType == OciMediaTypes.ImageManifest && doc.RootElement.TryGetProperty("config", out var configElement))
                    {
                        artifactType = configElement.GetProperty("mediaType").GetString() ?? "";
                    }

                    // Extract annotations
                    Dictionary<string, string>? annotations = null;
                    if (doc.RootElement.TryGetProperty("annotations", out var annotationsElement))
                    {
                        annotations = JsonSerializer.Deserialize<Dictionary<string, string>>(annotationsElement.GetRawText());
                    }

                    if (string.IsNullOrEmpty(artifactType))
                        artifactType = null!;

                    await AddReferrerAsync(repository, subjectDigest, manifestDigest, artifactType, mediaType, annotations, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process subject relationship for manifest {Digest}", manifestDigest);
        }
    }

    private async Task CleanupReferrerRelationshipsAsync(string repository, string manifestDigest, CancellationToken cancellationToken)
    {
        try
        {
            // This is a simplified cleanup - in practice, you'd need to scan all referrers
            // to find ones that reference this manifest as a subject
            var referrersDir = Path.Combine(_repositoriesPath, repository, "referrers");
            if (!Directory.Exists(referrersDir))
                return;

            foreach (var referrersFile in Directory.GetFiles(referrersDir))
            {
                var subjectDigest = Path.GetFileName(referrersFile);
                await RemoveReferrerAsync(repository, subjectDigest, manifestDigest, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup referrer relationships for manifest {Digest}", manifestDigest);
        }
    }

    private static string ComputeDigest(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return $"sha256:{hashString}";
    }

    private static bool IsDigest(string reference)
    {
        return reference.Contains(':') && reference.Split(':').Length == 2;
    }

    private static string DetermineMediaType(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("mediaType", out var mediaTypeElement))
            {
                return mediaTypeElement.GetString() ?? OciMediaTypes.ImageManifest;
            }

            // Determine by structure
            if (doc.RootElement.TryGetProperty("manifests", out _))
            {
                return OciMediaTypes.ImageIndex;
            }

            return OciMediaTypes.ImageManifest;
        }
        catch
        {
            return OciMediaTypes.ImageManifest;
        }
    }
}

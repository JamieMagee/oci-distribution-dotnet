namespace OciDistributionRegistry.Repositories;

/// <summary>
/// Repository interface for manifest storage operations.
/// </summary>
public interface IManifestRepository
{
    /// <summary>
    /// Checks if a manifest exists in the repository.
    /// </summary>
    /// <param name="repository">The repository name</param>
    /// <param name="reference">The manifest reference (tag or digest)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the manifest exists, false otherwise</returns>
    Task<bool> ExistsAsync(string repository, string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a manifest from the repository.
    /// </summary>
    /// <param name="repository">The repository name</param>
    /// <param name="reference">The manifest reference (tag or digest)</param>
    /// <param name="acceptTypes">Accepted media types</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The manifest data and media type, or null if not found</returns>
    Task<(byte[] Data, string MediaType, string Digest)?> GetAsync(string repository, string reference, string[]? acceptTypes = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a manifest in the repository.
    /// </summary>
    /// <param name="repository">The repository name</param>
    /// <param name="reference">The manifest reference (tag or digest)</param>
    /// <param name="data">The manifest data</param>
    /// <param name="mediaType">The manifest media type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The manifest digest</returns>
    Task<string> StoreAsync(string repository, string reference, byte[] data, string mediaType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a manifest from the repository.
    /// </summary>
    /// <param name="repository">The repository name</param>
    /// <param name="reference">The manifest reference (tag or digest)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the manifest was deleted, false if not found</returns>
    Task<bool> DeleteAsync(string repository, string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of tags for a repository.
    /// </summary>
    /// <param name="repository">The repository name</param>
    /// <param name="n">Maximum number of tags to return</param>
    /// <param name="last">Last tag for pagination</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The list of tags and next page info</returns>
    Task<(string[] Tags, string? NextTag)> GetTagsAsync(string repository, int? n = null, string? last = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of referrers for a manifest.
    /// </summary>
    /// <param name="repository">The repository name</param>
    /// <param name="digest">The subject digest</param>
    /// <param name="artifactType">Optional artifact type filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The referrers index</returns>
    Task<byte[]> GetReferrersAsync(string repository, string digest, string? artifactType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a referrer relationship.
    /// </summary>
    /// <param name="repository">The repository name</param>
    /// <param name="subjectDigest">The subject digest</param>
    /// <param name="referrerDigest">The referrer digest</param>
    /// <param name="artifactType">The artifact type</param>
    /// <param name="annotations">Optional annotations</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task AddReferrerAsync(string repository, string subjectDigest, string referrerDigest, string artifactType, string mediaType, Dictionary<string, string>? annotations = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a referrer relationship.
    /// </summary>
    /// <param name="repository">The repository name</param>
    /// <param name="subjectDigest">The subject digest</param>
    /// <param name="referrerDigest">The referrer digest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task RemoveReferrerAsync(string repository, string subjectDigest, string referrerDigest, CancellationToken cancellationToken = default);
}

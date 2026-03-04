namespace OciDistributionRegistry.Repositories;

/// <summary>
/// Repository interface for blob storage operations.
/// </summary>
public interface IBlobRepository
{
    /// <summary>
    /// Checks if a blob exists in the repository.
    /// </summary>
    /// <param name="digest">The blob digest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the blob exists, false otherwise</returns>
    Task<bool> ExistsAsync(string digest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a blob from the repository.
    /// </summary>
    /// <param name="digest">The blob digest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The blob stream, or null if not found</returns>
    Task<Stream?> GetAsync(string digest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a blob from the repository with range support.
    /// </summary>
    /// <param name="digest">The blob digest</param>
    /// <param name="start">The start position</param>
    /// <param name="end">The end position (inclusive)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The blob stream portion, or null if not found</returns>
    Task<Stream?> GetRangeAsync(string digest, long start, long? end = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the size of a blob.
    /// </summary>
    /// <param name="digest">The blob digest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The blob size, or null if not found</returns>
    Task<long?> GetSizeAsync(string digest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a blob in the repository.
    /// </summary>
    /// <param name="digest">The blob digest</param>
    /// <param name="stream">The blob stream</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task StoreAsync(string digest, Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob from the repository.
    /// </summary>
    /// <param name="digest">The blob digest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the blob was deleted, false if not found</returns>
    Task<bool> DeleteAsync(string digest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a blob upload session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The upload session ID</returns>
    Task<string> InitiateUploadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes data to an upload session.
    /// </summary>
    /// <param name="sessionId">The upload session ID</param>
    /// <param name="stream">The data stream</param>
    /// <param name="contentRange">The content range (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The current upload range</returns>
    Task<(long Start, long End)> WriteUploadAsync(string sessionId, Stream stream, string? contentRange = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes an upload session.
    /// </summary>
    /// <param name="sessionId">The upload session ID</param>
    /// <param name="digest">The expected digest</param>
    /// <param name="stream">Final data stream (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task CompleteUploadAsync(string sessionId, string digest, Stream? stream = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of an upload session.
    /// </summary>
    /// <param name="sessionId">The upload session ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The current upload range, or null if session not found</returns>
    Task<(long Start, long End)?> GetUploadStatusAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an upload session.
    /// </summary>
    /// <param name="sessionId">The upload session ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task CancelUploadAsync(string sessionId, CancellationToken cancellationToken = default);
}

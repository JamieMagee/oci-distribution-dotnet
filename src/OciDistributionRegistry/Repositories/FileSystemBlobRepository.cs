using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace OciDistributionRegistry.Repositories;

/// <summary>
/// File system-based implementation of the blob repository.
/// </summary>
public class FileSystemBlobRepository : IBlobRepository
{
    private readonly string _blobsPath;
    private readonly string _uploadsPath;
    private readonly ConcurrentDictionary<string, UploadSession> _uploadSessions = new();
    private readonly ILogger<FileSystemBlobRepository> _logger;

    public FileSystemBlobRepository(
        IConfiguration configuration,
        ILogger<FileSystemBlobRepository> logger
    )
    {
        _logger = logger;
        var storagePath = configuration.GetValue<string>("Storage:Path") ?? "/tmp/oci-registry";
        _blobsPath = Path.Combine(storagePath, "blobs");
        _uploadsPath = Path.Combine(storagePath, "uploads");

        Directory.CreateDirectory(_blobsPath);
        Directory.CreateDirectory(_uploadsPath);
    }

    public async Task<bool> ExistsAsync(
        string digest,
        CancellationToken cancellationToken = default
    )
    {
        var path = GetBlobPath(digest);
        return File.Exists(path);
    }

    public async Task<Stream?> GetAsync(
        string digest,
        CancellationToken cancellationToken = default
    )
    {
        var path = GetBlobPath(digest);
        if (!File.Exists(path))
            return null;

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public async Task<Stream?> GetRangeAsync(
        string digest,
        long start,
        long? end = null,
        CancellationToken cancellationToken = default
    )
    {
        var path = GetBlobPath(digest);
        if (!File.Exists(path))
            return null;

        var fileInfo = new FileInfo(path);
        var fileSize = fileInfo.Length;

        if (start >= fileSize)
            throw new ArgumentOutOfRangeException(nameof(start));

        var actualEnd = end ?? fileSize - 1;
        if (actualEnd >= fileSize)
            actualEnd = fileSize - 1;

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(start, SeekOrigin.Begin);

        var length = actualEnd - start + 1;
        return new LimitedStream(stream, length);
    }

    public async Task<long?> GetSizeAsync(
        string digest,
        CancellationToken cancellationToken = default
    )
    {
        var path = GetBlobPath(digest);
        if (!File.Exists(path))
            return null;

        return new FileInfo(path).Length;
    }

    public async Task StoreAsync(
        string digest,
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        var path = GetBlobPath(digest);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);

        // Verify digest
        var actualDigest = await ComputeDigestAsync(path, cancellationToken);
        if (actualDigest != digest)
        {
            File.Delete(path);
            throw new InvalidOperationException(
                $"Digest mismatch: expected {digest}, got {actualDigest}"
            );
        }

        _logger.LogInformation("Stored blob {Digest} at {Path}", digest, path);
    }

    public async Task<bool> DeleteAsync(
        string digest,
        CancellationToken cancellationToken = default
    )
    {
        var path = GetBlobPath(digest);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        _logger.LogInformation("Deleted blob {Digest} from {Path}", digest, path);
        return true;
    }

    public async Task<string> InitiateUploadAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var uploadPath = Path.Combine(_uploadsPath, sessionId);

        var session = new UploadSession
        {
            Id = sessionId,
            Path = uploadPath,
            CreatedAt = DateTime.UtcNow,
            Position = 0,
        };

        _uploadSessions[sessionId] = session;
        _logger.LogInformation("Initiated upload session {SessionId}", sessionId);

        return sessionId;
    }

    public async Task<(long Start, long End)> WriteUploadAsync(
        string sessionId,
        Stream stream,
        string? contentRange = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!_uploadSessions.TryGetValue(sessionId, out var session))
            throw new InvalidOperationException($"Upload session {sessionId} not found");

        var expectedStart = session.Position;

        // Parse content range if provided
        if (!string.IsNullOrEmpty(contentRange))
        {
            var parts = contentRange.Split('-');
            if (parts.Length == 2 && long.TryParse(parts[0], out var rangeStart))
            {
                if (rangeStart != expectedStart)
                    throw new InvalidOperationException(
                        $"Range start {rangeStart} does not match expected position {expectedStart}"
                    );
            }
        }

        var startPosition = new FileInfo(session.Path).Exists
            ? new FileInfo(session.Path).Length
            : 0;
        using var fileStream = new FileStream(session.Path, FileMode.Append, FileAccess.Write);
        await stream.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);

        var bytesWritten = fileStream.Length - startPosition;
        session.Position += bytesWritten;

        _logger.LogDebug(
            "Wrote {BytesWritten} bytes to upload session {SessionId}",
            bytesWritten,
            sessionId
        );

        return (expectedStart, session.Position - 1);
    }

    public async Task CompleteUploadAsync(
        string sessionId,
        string digest,
        Stream? stream = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!_uploadSessions.TryRemove(sessionId, out var session))
            throw new InvalidOperationException($"Upload session {sessionId} not found");

        try
        {
            // Write final chunk if provided
            if (stream != null)
            {
                using var fileStream = new FileStream(
                    session.Path,
                    FileMode.Append,
                    FileAccess.Write
                );
                await stream.CopyToAsync(fileStream, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);
            }

            // Verify digest
            var actualDigest = await ComputeDigestAsync(session.Path, cancellationToken);
            if (actualDigest != digest)
            {
                throw new InvalidOperationException(
                    $"Digest mismatch: expected {digest}, got {actualDigest}"
                );
            }

            // Move to final location
            var finalPath = GetBlobPath(digest);
            var directory = Path.GetDirectoryName(finalPath)!;
            Directory.CreateDirectory(directory);

            File.Move(session.Path, finalPath, overwrite: true);
            _logger.LogInformation(
                "Completed upload session {SessionId} for blob {Digest}",
                sessionId,
                digest
            );
        }
        catch
        {
            // Clean up on error
            if (File.Exists(session.Path))
                File.Delete(session.Path);
            throw;
        }
    }

    public async Task<(long Start, long End)?> GetUploadStatusAsync(
        string sessionId,
        CancellationToken cancellationToken = default
    )
    {
        if (!_uploadSessions.TryGetValue(sessionId, out var session))
            return null;

        return (0, session.Position - 1);
    }

    public async Task CancelUploadAsync(
        string sessionId,
        CancellationToken cancellationToken = default
    )
    {
        if (_uploadSessions.TryRemove(sessionId, out var session))
        {
            if (File.Exists(session.Path))
                File.Delete(session.Path);

            _logger.LogInformation("Cancelled upload session {SessionId}", sessionId);
        }
    }

    private string GetBlobPath(string digest)
    {
        var parts = digest.Split(':');
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid digest format: {digest}");

        var algorithm = parts[0];
        var hash = parts[1];

        // Use first two characters for directory structure
        var dir1 = hash.Substring(0, 2);
        var dir2 = hash.Substring(2, 2);

        return Path.Combine(_blobsPath, algorithm, dir1, dir2, hash);
    }

    private async Task<string> ComputeDigestAsync(
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return $"sha256:{hashString}";
    }

    private class UploadSession
    {
        public required string Id { get; set; }
        public required string Path { get; set; }
        public DateTime CreatedAt { get; set; }
        public long Position { get; set; }
    }

    private class LimitedStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _maxLength;
        private long _position;

        public LimitedStream(Stream baseStream, long maxLength)
        {
            _baseStream = baseStream;
            _maxLength = maxLength;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _maxLength;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _maxLength - _position;
            if (remaining <= 0)
                return 0;

            var toRead = (int)Math.Min(count, remaining);
            var bytesRead = _baseStream.Read(buffer, offset, toRead);
            _position += bytesRead;
            return bytesRead;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            var remaining = _maxLength - _position;
            if (remaining <= 0)
                return 0;

            var toRead = (int)Math.Min(count, remaining);
            var bytesRead = await _baseStream.ReadAsync(buffer, offset, toRead, cancellationToken);
            _position += bytesRead;
            return bytesRead;
        }

        public override void Flush() => _baseStream.Flush();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _baseStream?.Dispose();
            base.Dispose(disposing);
        }
    }
}

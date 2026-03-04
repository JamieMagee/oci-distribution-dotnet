using Microsoft.AspNetCore.Mvc;
using OciDistributionRegistry.Models;
using OciDistributionRegistry.Repositories;
using OciDistributionRegistry.Services;

namespace OciDistributionRegistry.Controllers;

/// <summary>
/// Controller for blob operations according to OCI Distribution Specification.
/// </summary>
[ApiController]
[Route("/v2/{name}/blobs")]
public class BlobsController : DistributionBaseController
{
    private readonly IBlobRepository _blobRepository;
    private readonly IValidationService _validationService;

    public BlobsController(
        IBlobRepository blobRepository,
        IValidationService validationService,
        ILogger<BlobsController> logger
    )
        : base(logger)
    {
        _blobRepository = blobRepository;
        _validationService = validationService;
    }

    /// <summary>
    /// Retrieve a blob from the registry.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="digest">Blob digest</param>
    /// <returns>The blob content</returns>
    /// <response code="200">Blob retrieved successfully</response>
    /// <response code="404">Blob not found</response>
    [HttpGet("{digest}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBlob(string name, string digest)
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        if (!_validationService.IsValidDigest(digest))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.DigestInvalid, "Invalid digest format")
            );
        }

        Logger.LogDebug("Getting blob {Digest} from repository {Repository}", digest, name);

        var size = await _blobRepository.GetSizeAsync(digest);

        var rangeHeader = Request.Headers["Range"].ToString();
        if (!string.IsNullOrEmpty(rangeHeader) && size.HasValue)
        {
            // Parse "bytes=start-end" per RFC 9110
            var rangeSpec = rangeHeader.Replace("bytes=", "");
            var parts = rangeSpec.Split('-');
            var start = long.Parse(parts[0]);
            var end =
                parts.Length > 1 && !string.IsNullOrEmpty(parts[1])
                    ? long.Parse(parts[1])
                    : size.Value - 1;

            var rangeStream = await _blobRepository.GetRangeAsync(digest, start, end);
            if (rangeStream == null)
            {
                return NotFound(CreateErrorResponse(OciErrorCodes.BlobUnknown, "Blob not found"));
            }

            AddDockerHeaders(digest);
            Response.Headers["Content-Range"] = $"bytes {start}-{end}/{size.Value}";
            Response.Headers["Content-Length"] = (end - start + 1).ToString();

            return File(rangeStream, "application/octet-stream");
        }

        var blobStream = await _blobRepository.GetAsync(digest);
        if (blobStream == null)
        {
            return NotFound(CreateErrorResponse(OciErrorCodes.BlobUnknown, "Blob not found"));
        }

        AddDockerHeaders(digest);
        Response.Headers["Content-Length"] = size.ToString();

        return File(blobStream, "application/octet-stream");
    }

    /// <summary>
    /// Check if a blob exists in the registry.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="digest">Blob digest</param>
    /// <returns>Headers indicating blob existence and size</returns>
    /// <response code="200">Blob exists</response>
    /// <response code="404">Blob not found</response>
    [HttpHead("{digest}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HeadBlob(string name, string digest)
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        if (!_validationService.IsValidDigest(digest))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.DigestInvalid, "Invalid digest format")
            );
        }

        Logger.LogDebug("Checking blob {Digest} in repository {Repository}", digest, name);

        var exists = await _blobRepository.ExistsAsync(digest);
        if (!exists)
        {
            return NotFound();
        }

        var size = await _blobRepository.GetSizeAsync(digest);

        AddDockerHeaders(digest);
        Response.Headers["Content-Length"] = size.ToString();

        return Ok();
    }

    /// <summary>
    /// Delete a blob from the registry.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="digest">Blob digest</param>
    /// <returns>Deletion confirmation</returns>
    /// <response code="202">Blob deletion accepted</response>
    /// <response code="404">Blob not found</response>
    /// <response code="405">Blob deletion not allowed</response>
    [HttpDelete("{digest}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status405MethodNotAllowed)]
    public async Task<IActionResult> DeleteBlob(string name, string digest)
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        if (!_validationService.IsValidDigest(digest))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.DigestInvalid, "Invalid digest format")
            );
        }

        Logger.LogInformation("Deleting blob {Digest} from repository {Repository}", digest, name);

        var deleted = await _blobRepository.DeleteAsync(digest);
        if (!deleted)
        {
            return NotFound(CreateErrorResponse(OciErrorCodes.BlobUnknown, "Blob not found"));
        }

        AddDockerHeaders();
        return Accepted();
    }

    /// <summary>
    /// Initiate a blob upload.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="digest">Optional digest for monolithic upload</param>
    /// <param name="mount">Optional digest to mount from another repository</param>
    /// <param name="from">Source repository for mounting</param>
    /// <returns>Upload location or blob location if already exists</returns>
    /// <response code="201">Blob uploaded successfully (monolithic)</response>
    /// <response code="202">Upload session created</response>
    /// <response code="404">Repository not found</response>
    [HttpPost("uploads/")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> InitiateUpload(
        string name,
        [FromQuery] string? digest = null,
        [FromQuery] string? mount = null,
        [FromQuery] string? from = null
    )
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        Logger.LogDebug(
            "Initiating upload for repository {Repository}, digest={Digest}, mount={Mount}, from={From}",
            name,
            digest,
            mount,
            from
        );

        // Handle blob mounting
        if (!string.IsNullOrEmpty(mount) && !string.IsNullOrEmpty(from))
        {
            return await HandleBlobMount(name, mount, from);
        }

        // Handle monolithic upload
        if (!string.IsNullOrEmpty(digest))
        {
            return await HandleMonolithicUpload(name, digest);
        }

        // Initiate chunked upload
        var sessionId = await _blobRepository.InitiateUploadAsync();
        var location = $"/v2/{name}/blobs/uploads/{sessionId}";

        AddDockerHeaders();
        Response.Headers["Location"] = location;
        Response.Headers["Range"] = "0-0";
        Response.Headers["OCI-Chunk-Min-Length"] = "0";

        return Accepted();
    }

    /// <summary>
    /// Upload a chunk of a blob.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="uuid">Upload session ID</param>
    /// <returns>Current upload status</returns>
    /// <response code="202">Chunk uploaded successfully</response>
    /// <response code="404">Upload session not found</response>
    /// <response code="416">Range not satisfiable</response>
    [HttpPatch("uploads/{uuid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status416RequestedRangeNotSatisfiable)]
    public async Task<IActionResult> UploadChunk(string name, string uuid)
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        var contentRange = Request.Headers["Content-Range"].ToString();
        var contentLength = Request.ContentLength ?? 0;

        // For streamed (non-chunked) uploads, Content-Range may be absent.
        // Treat as appending from the current position.
        if (string.IsNullOrEmpty(contentRange))
        {
            contentRange = null;
        }
        else
        {
            var rangeValidation = _validationService.ValidateContentRange(
                contentRange,
                contentLength
            );
            if (!rangeValidation.IsValid)
            {
                return BadRequest(
                    CreateErrorResponse(OciErrorCodes.SizeInvalid, rangeValidation.ErrorMessage)
                );
            }
        }

        Logger.LogDebug(
            "Uploading chunk for session {SessionId}, range {Range}",
            uuid,
            contentRange ?? "(streamed)"
        );

        try
        {
            var (start, end) = await _blobRepository.WriteUploadAsync(
                uuid,
                Request.Body,
                contentRange
            );

            var location = $"/v2/{name}/blobs/uploads/{uuid}";
            AddDockerHeaders();
            Response.Headers["Location"] = location;
            Response.Headers["Range"] = $"0-{end}";

            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning(ex, "Upload chunk failed for session {SessionId}", uuid);

            if (ex.Message.Contains("not found"))
                return NotFound(
                    CreateErrorResponse(OciErrorCodes.BlobUploadUnknown, "Upload session not found")
                );

            if (ex.Message.Contains("Range"))
                return StatusCode(
                    416,
                    CreateErrorResponse(OciErrorCodes.BlobUploadInvalid, ex.Message)
                );

            return BadRequest(CreateErrorResponse(OciErrorCodes.BlobUploadInvalid, ex.Message));
        }
    }

    /// <summary>
    /// Complete a blob upload.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="uuid">Upload session ID</param>
    /// <param name="digest">Expected blob digest</param>
    /// <returns>Blob location</returns>
    /// <response code="201">Blob upload completed successfully</response>
    /// <response code="400">Invalid digest or upload data</response>
    /// <response code="404">Upload session not found</response>
    [HttpPut("uploads/{uuid}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteUpload(
        string name,
        string uuid,
        [FromQuery] string digest
    )
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        if (!_validationService.IsValidDigest(digest))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.DigestInvalid, "Invalid digest format")
            );
        }

        Logger.LogInformation(
            "Completing upload session {SessionId} with digest {Digest}",
            uuid,
            digest
        );

        try
        {
            await _blobRepository.CompleteUploadAsync(uuid, digest, Request.Body);

            var location = $"/v2/{name}/blobs/{digest}";
            AddDockerHeaders(digest);
            Response.Headers["Location"] = location;

            return Created(location, null);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning(ex, "Upload completion failed for session {SessionId}", uuid);

            if (ex.Message.Contains("not found"))
                return NotFound(
                    CreateErrorResponse(OciErrorCodes.BlobUploadUnknown, "Upload session not found")
                );

            if (ex.Message.Contains("Digest mismatch"))
                return BadRequest(CreateErrorResponse(OciErrorCodes.DigestInvalid, ex.Message));

            return BadRequest(CreateErrorResponse(OciErrorCodes.BlobUploadInvalid, ex.Message));
        }
    }

    /// <summary>
    /// Get the status of an upload session.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="uuid">Upload session ID</param>
    /// <returns>Current upload status</returns>
    /// <response code="204">Upload session status</response>
    /// <response code="404">Upload session not found</response>
    [HttpGet("uploads/{uuid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUploadStatus(string name, string uuid)
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        Logger.LogDebug("Getting upload status for session {SessionId}", uuid);

        var status = await _blobRepository.GetUploadStatusAsync(uuid);
        if (status == null)
        {
            return NotFound(
                CreateErrorResponse(OciErrorCodes.BlobUploadUnknown, "Upload session not found")
            );
        }

        var location = $"/v2/{name}/blobs/uploads/{uuid}";
        AddDockerHeaders();
        Response.Headers["Location"] = location;
        Response.Headers["Range"] = $"{status.Value.Start}-{status.Value.End}";

        return NoContent();
    }

    /// <summary>
    /// Cancel a blob upload session.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="uuid">Upload session ID</param>
    /// <returns>No content on success</returns>
    /// <response code="204">Upload session cancelled</response>
    [HttpDelete("uploads/{uuid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelUpload(string name, string uuid)
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        Logger.LogInformation(
            "Cancelling upload session {SessionId} for repository {Repository}",
            uuid,
            name
        );

        await _blobRepository.CancelUploadAsync(uuid);

        AddDockerHeaders();
        return NoContent();
    }

    private async Task<IActionResult> HandleBlobMount(string name, string mount, string from)
    {
        if (!_validationService.IsValidDigest(mount))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.DigestInvalid, "Invalid mount digest")
            );
        }

        // Check if blob exists in source repository
        var exists = await _blobRepository.ExistsAsync(mount);
        if (!exists)
        {
            // Fallback to regular upload
            var sessionId = await _blobRepository.InitiateUploadAsync();
            var location = $"/v2/{name}/blobs/uploads/{sessionId}";

            AddDockerHeaders();
            Response.Headers["Location"] = location;

            return Accepted();
        }

        // Blob exists, return success
        var blobLocation = $"/v2/{name}/blobs/{mount}";
        AddDockerHeaders(mount);
        Response.Headers["Location"] = blobLocation;

        return Created(blobLocation, null);
    }

    private async Task<IActionResult> HandleMonolithicUpload(string name, string digest)
    {
        if (!_validationService.IsValidDigest(digest))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.DigestInvalid, "Invalid digest format")
            );
        }

        var contentLength = Request.ContentLength ?? 0;
        if (contentLength == 0)
        {
            return BadRequest(
                CreateErrorResponse(
                    OciErrorCodes.SizeInvalid,
                    "Content-Length required for monolithic upload"
                )
            );
        }

        // Read and validate content
        using var memoryStream = new MemoryStream();
        await Request.Body.CopyToAsync(memoryStream);
        var data = memoryStream.ToArray();

        if (!_validationService.VerifyDigest(data, digest))
        {
            return BadRequest(CreateErrorResponse(OciErrorCodes.DigestInvalid, "Digest mismatch"));
        }

        // Store blob
        memoryStream.Position = 0;
        await _blobRepository.StoreAsync(digest, memoryStream);

        var location = $"/v2/{name}/blobs/{digest}";
        AddDockerHeaders(digest);
        Response.Headers["Location"] = location;

        return Created(location, null);
    }
}

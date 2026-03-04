using Microsoft.AspNetCore.Mvc;
using OciDistributionRegistry.Models;
using OciDistributionRegistry.Repositories;
using OciDistributionRegistry.Services;

namespace OciDistributionRegistry.Controllers;

/// <summary>
/// Controller for manifest operations according to OCI Distribution Specification.
/// </summary>
[ApiController]
[Route("/v2/{name}/manifests")]
public class ManifestsController : DistributionBaseController
{
    private readonly IManifestRepository _manifestRepository;
    private readonly IValidationService _validationService;

    public ManifestsController(
        IManifestRepository manifestRepository,
        IValidationService validationService,
        ILogger<ManifestsController> logger
    )
        : base(logger)
    {
        _manifestRepository = manifestRepository;
        _validationService = validationService;
    }

    /// <summary>
    /// Retrieve a manifest from the registry.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="reference">Manifest reference (tag or digest)</param>
    /// <returns>The manifest content</returns>
    /// <response code="200">Manifest retrieved successfully</response>
    /// <response code="404">Manifest not found</response>
    [HttpGet("{reference}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetManifest(string name, string reference)
    {
        var repoValidation = ValidateRepositoryName(name);
        if (repoValidation != null)
            return repoValidation;

        if (!IsValidReference(reference))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.NameInvalid, "Invalid reference format")
            );
        }

        Logger.LogDebug(
            "Getting manifest {Reference} from repository {Repository}",
            reference,
            name
        );

        // Get accepted media types from Accept header
        var acceptHeader = Request.Headers["Accept"].ToString();
        var acceptTypes = ParseAcceptHeader(acceptHeader);

        var result = await _manifestRepository.GetAsync(name, reference, acceptTypes);
        if (result == null)
        {
            return NotFound(
                CreateErrorResponse(OciErrorCodes.ManifestUnknown, "Manifest not found")
            );
        }

        var (data, mediaType, digest) = result.Value;

        AddDockerHeaders(digest);
        Response.Headers.Add("Content-Length", data.Length.ToString());
        Response.ContentType = mediaType;

        return File(data, mediaType);
    }

    /// <summary>
    /// Check if a manifest exists in the registry.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="reference">Manifest reference (tag or digest)</param>
    /// <returns>Headers indicating manifest existence</returns>
    /// <response code="200">Manifest exists</response>
    /// <response code="404">Manifest not found</response>
    [HttpHead("{reference}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HeadManifest(string name, string reference)
    {
        var repoValidation = ValidateRepositoryName(name);
        if (repoValidation != null)
            return repoValidation;

        if (!IsValidReference(reference))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.NameInvalid, "Invalid reference format")
            );
        }

        Logger.LogDebug(
            "Checking manifest {Reference} in repository {Repository}",
            reference,
            name
        );

        var acceptHeader = Request.Headers["Accept"].ToString();
        var acceptTypes = ParseAcceptHeader(acceptHeader);

        var result = await _manifestRepository.GetAsync(name, reference, acceptTypes);
        if (result == null)
        {
            return NotFound();
        }

        var (data, mediaType, digest) = result.Value;

        AddDockerHeaders(digest);
        Response.Headers.Add("Content-Length", data.Length.ToString());
        Response.ContentType = mediaType;

        return Ok();
    }

    /// <summary>
    /// Store a manifest in the registry.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="reference">Manifest reference (tag or digest)</param>
    /// <returns>Manifest location</returns>
    /// <response code="201">Manifest stored successfully</response>
    /// <response code="400">Invalid manifest</response>
    /// <response code="413">Manifest too large</response>
    [HttpPut("{reference}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> PutManifest(string name, string reference)
    {
        var repoValidation = ValidateRepositoryName(name);
        if (repoValidation != null)
            return repoValidation;

        if (!IsValidReference(reference))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.NameInvalid, "Invalid reference format")
            );
        }

        var contentType = Request.ContentType ?? "";
        var contentLength = Request.ContentLength ?? 0;

        // Check manifest size limit (4MB default)
        const int maxManifestSize = 4 * 1024 * 1024;
        if (contentLength > maxManifestSize)
        {
            return StatusCode(
                413,
                CreateErrorResponse(OciErrorCodes.ManifestInvalid, "Manifest too large")
            );
        }

        Logger.LogInformation(
            "Storing manifest {Reference} in repository {Repository}",
            reference,
            name
        );

        // Read manifest data
        using var memoryStream = new MemoryStream();
        await Request.Body.CopyToAsync(memoryStream);
        var manifestData = memoryStream.ToArray();

        // Validate manifest
        var validation = await _validationService.ValidateManifestAsync(manifestData, contentType);
        if (!validation.IsValid)
        {
            return BadRequest(
                CreateErrorResponse(
                    validation.ErrorCode ?? OciErrorCodes.ManifestInvalid,
                    validation.ErrorMessage
                )
            );
        }

        // Compute digest and validate if reference is a digest
        var computedDigest = _validationService.ComputeDigest(manifestData);
        if (_validationService.IsValidDigest(reference) && reference != computedDigest)
        {
            return BadRequest(
                CreateErrorResponse(
                    OciErrorCodes.DigestInvalid,
                    "Reference digest does not match content"
                )
            );
        }

        try
        {
            var digest = await _manifestRepository.StoreAsync(
                name,
                reference,
                manifestData,
                contentType
            );

            var location = $"/v2/{name}/manifests/{digest}";
            AddDockerHeaders(digest);
            Response.Headers.Add("Location", location);

            // Add OCI-Subject header if manifest has subject field
            var subjectDigest = ExtractSubjectDigest(manifestData);
            if (!string.IsNullOrEmpty(subjectDigest))
            {
                Response.Headers.Add("OCI-Subject", subjectDigest);
            }

            return Created(location, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to store manifest {Reference} in repository {Repository}",
                reference,
                name
            );
            return StatusCode(
                500,
                CreateErrorResponse(OciErrorCodes.ManifestInvalid, "Failed to store manifest")
            );
        }
    }

    /// <summary>
    /// Delete a manifest from the registry.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="reference">Manifest reference (tag or digest)</param>
    /// <returns>Deletion confirmation</returns>
    /// <response code="202">Manifest deletion accepted</response>
    /// <response code="404">Manifest not found</response>
    /// <response code="405">Manifest deletion not allowed</response>
    [HttpDelete("{reference}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status405MethodNotAllowed)]
    public async Task<IActionResult> DeleteManifest(string name, string reference)
    {
        var repoValidation = ValidateRepositoryName(name);
        if (repoValidation != null)
            return repoValidation;

        if (!IsValidReference(reference))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.NameInvalid, "Invalid reference format")
            );
        }

        Logger.LogInformation(
            "Deleting manifest {Reference} from repository {Repository}",
            reference,
            name
        );

        var deleted = await _manifestRepository.DeleteAsync(name, reference);
        if (!deleted)
        {
            return NotFound(
                CreateErrorResponse(OciErrorCodes.ManifestUnknown, "Manifest not found")
            );
        }

        AddDockerHeaders();
        return Accepted();
    }

    private bool IsValidReference(string reference)
    {
        if (string.IsNullOrEmpty(reference))
            return false;

        // Check if it's a valid digest
        if (_validationService.IsValidDigest(reference))
            return true;

        // Check if it's a valid tag
        return _validationService.IsValidTag(reference);
    }

    private string[] ParseAcceptHeader(string acceptHeader)
    {
        if (string.IsNullOrEmpty(acceptHeader))
            return Array.Empty<string>();

        return acceptHeader
            .Split(',')
            .Select(s => s.Trim().Split(';')[0]) // Remove quality values
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }

    private string? ExtractSubjectDigest(byte[] manifestData)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(manifestData);
            if (
                doc.RootElement.TryGetProperty("subject", out var subjectElement)
                && subjectElement.TryGetProperty("digest", out var digestElement)
            )
            {
                return digestElement.GetString();
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }
}

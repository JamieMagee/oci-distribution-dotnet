using Microsoft.AspNetCore.Mvc;
using OciDistributionRegistry.Models;
using OciDistributionRegistry.Repositories;
using OciDistributionRegistry.Services;

namespace OciDistributionRegistry.Controllers;

/// <summary>
/// Controller for tag and referrer operations according to OCI Distribution Specification.
/// </summary>
[ApiController]
[Route("/v2/{name}")]
public class RepositoryController : DistributionBaseController
{
    private readonly IManifestRepository _manifestRepository;
    private readonly IValidationService _validationService;

    public RepositoryController(
        IManifestRepository manifestRepository,
        IValidationService validationService,
        ILogger<RepositoryController> logger
    )
        : base(logger)
    {
        _manifestRepository = manifestRepository;
        _validationService = validationService;
    }

    /// <summary>
    /// List tags for a repository.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="n">Maximum number of tags to return</param>
    /// <param name="last">Last tag for pagination</param>
    /// <returns>List of tags</returns>
    /// <response code="200">Tags retrieved successfully</response>
    /// <response code="404">Repository not found</response>
    [HttpGet("tags/list")]
    [ProducesResponseType(typeof(TagList), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListTags(
        string name,
        [FromQuery] int? n = null,
        [FromQuery] string? last = null
    )
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        Logger.LogDebug(
            "Listing tags for repository {Repository}, n={N}, last={Last}",
            name,
            n,
            last
        );

        // Validate pagination parameters
        if (n.HasValue && n.Value < 0)
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.NameInvalid, "Parameter 'n' must be non-negative")
            );
        }

        if (n.HasValue && n.Value == 0)
        {
            // Return empty list when n=0
            var emptyResponse = new TagList { Name = name, Tags = Array.Empty<string>() };
            return Ok(emptyResponse);
        }

        try
        {
            var (tags, nextTag) = await _manifestRepository.GetTagsAsync(name, n, last);

            var response = new TagList { Name = name, Tags = tags };

            // Add Link header for pagination if there are more tags
            if (!string.IsNullOrEmpty(nextTag))
            {
                var linkUrl = $"/v2/{name}/tags/list?n={n ?? tags.Length}&last={nextTag}";
                Response.Headers.Add("Link", $"<{linkUrl}>; rel=\"next\"");
            }

            AddDockerHeaders();
            return Ok(response);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to list tags for repository {Repository}", name);
            return NotFound(CreateErrorResponse(OciErrorCodes.NameUnknown, "Repository not found"));
        }
    }

    /// <summary>
    /// List referrers for a manifest.
    /// </summary>
    /// <param name="name">Repository name</param>
    /// <param name="digest">Subject manifest digest</param>
    /// <param name="artifactType">Optional artifact type filter</param>
    /// <returns>List of referrers as an OCI Index</returns>
    /// <response code="200">Referrers retrieved successfully</response>
    /// <response code="400">Invalid digest format</response>
    [HttpGet("referrers/{digest}")]
    [ProducesResponseType(typeof(ImageIndex), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListReferrers(
        string name,
        string digest,
        [FromQuery] string? artifactType = null
    )
    {
        var repoValidation = ValidateRepositoryName(name, out name);
        if (repoValidation != null)
            return repoValidation;

        // Validate digest format
        if (!_validationService.IsValidDigest(digest))
        {
            return BadRequest(
                CreateErrorResponse(OciErrorCodes.DigestInvalid, "Invalid digest format")
            );
        }

        Logger.LogDebug(
            "Listing referrers for {Digest} in repository {Repository}, artifactType={ArtifactType}",
            digest,
            name,
            artifactType
        );

        try
        {
            var referrersData = await _manifestRepository.GetReferrersAsync(
                name,
                digest,
                artifactType
            );

            // Add OCI-Filters-Applied header if filtering was applied
            if (!string.IsNullOrEmpty(artifactType))
            {
                Response.Headers.Add("OCI-Filters-Applied", "artifactType");
            }

            Response.ContentType = OciMediaTypes.ImageIndex;
            AddDockerHeaders();

            return File(referrersData, OciMediaTypes.ImageIndex);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to list referrers for {Digest} in repository {Repository}",
                digest,
                name
            );
            var emptyIndex = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new Models.ImageIndex
                {
                    SchemaVersion = 2,
                    MediaType = Models.OciMediaTypes.ImageIndex,
                    Manifests = Array.Empty<Models.Descriptor>(),
                }
            );
            Response.ContentType = Models.OciMediaTypes.ImageIndex;
            AddDockerHeaders();
            return File(emptyIndex, Models.OciMediaTypes.ImageIndex);
        }
    }
}

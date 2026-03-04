using System.Net;
using System.Text.Json;
using OciDistributionRegistry.ConformanceTests.Helpers;
using Xunit;

namespace OciDistributionRegistry.ConformanceTests;

[Collection("Conformance")]
[TestCaseOrderer(typeof(AlphabeticalOrderer))]
public class ContentDiscoveryTests
{
    private readonly RegistryFixture _fixture;
    private readonly HttpClient _client;
    private readonly TestData _data;
    private readonly string _ns = RegistryFixture.Namespace;

    private const string ManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
    private const string IndexMediaType = "application/vnd.oci.image.index.v1+json";

    public ContentDiscoveryTests(RegistryFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _data = fixture.Data;
    }

    // ── A: Setup ─────────────────────────────────────────────────────

    [Fact]
    public async Task A01_Setup_PushConfigBlobs()
    {
        await TestData.PushBlobAsync(
            _client,
            _ns,
            _data.Configs[2].Content,
            _data.Configs[2].Digest
        );
        await TestData.PushBlobAsync(
            _client,
            _ns,
            _data.Configs[4].Content,
            _data.Configs[4].Digest
        );
    }

    [Fact]
    public async Task A02_Setup_PushLayerBlob()
    {
        await TestData.PushBlobAsync(_client, _ns, _data.LayerBlobData, _data.LayerBlobDigest);
    }

    [Fact]
    public async Task A03_Setup_PushEmptyJsonBlob()
    {
        await TestData.PushBlobAsync(_client, _ns, _data.EmptyJsonBlob, _data.EmptyJsonBlobDigest);
    }

    [Fact]
    public async Task A04_Setup_PushRefBlobs()
    {
        await TestData.PushBlobAsync(_client, _ns, _data.TestRefBlobA, _data.TestRefBlobADigest);
        await TestData.PushBlobAsync(_client, _ns, _data.TestRefBlobB, _data.TestRefBlobBDigest);
    }

    [Fact]
    public async Task A05_Setup_PushTaggedManifests()
    {
        string[] tags = ["test0", "TEST0", "test1", "TEST1", "test2", "TEST2", "test3", "TEST3"];
        foreach (var tag in tags)
        {
            var resp = await TestData.PushManifestAsync(
                _client,
                _ns,
                tag,
                _data.Manifests[2].Content,
                ManifestMediaType
            );
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }
    }

    [Fact]
    public async Task A06_Setup_PushManifest4WithTag()
    {
        var resp = await TestData.PushManifestAsync(
            _client,
            _ns,
            "tagtest0",
            _data.Manifests[4].Content,
            ManifestMediaType
        );
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task A07_Setup_PushRefsManifestAConfigArtifact()
    {
        var resp = await TestData.PushManifestAsync(
            _client,
            _ns,
            _data.RefsManifestAConfigArtifactDigest,
            _data.RefsManifestAConfigArtifactContent,
            ManifestMediaType
        );
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        if (resp.Headers.TryGetValues("OCI-Subject", out var values))
        {
            Assert.Equal(_data.Manifests[4].Digest, values.First());
        }
    }

    [Fact]
    public async Task A08_Setup_PushRefsManifestALayerArtifact()
    {
        var resp = await TestData.PushManifestAsync(
            _client,
            _ns,
            _data.RefsManifestALayerArtifactDigest,
            _data.RefsManifestALayerArtifactContent,
            ManifestMediaType
        );
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        if (resp.Headers.TryGetValues("OCI-Subject", out var values))
        {
            Assert.Equal(_data.Manifests[4].Digest, values.First());
        }
    }

    [Fact]
    public async Task A09_Setup_PushRefsIndexArtifact()
    {
        var resp = await TestData.PushManifestAsync(
            _client,
            _ns,
            _data.RefsIndexArtifactDigest,
            _data.RefsIndexArtifactContent,
            IndexMediaType
        );
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        if (resp.Headers.TryGetValues("OCI-Subject", out var values))
        {
            Assert.Equal(_data.Manifests[4].Digest, values.First());
        }
    }

    [Fact]
    public async Task A10_Setup_PushRefsManifestBConfigArtifact()
    {
        var resp = await TestData.PushManifestAsync(
            _client,
            _ns,
            _data.RefsManifestBConfigArtifactDigest,
            _data.RefsManifestBConfigArtifactContent,
            ManifestMediaType
        );
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task A11_Setup_PushRefsManifestBLayerArtifact()
    {
        var resp = await TestData.PushManifestAsync(
            _client,
            _ns,
            _data.RefsManifestBLayerArtifactDigest,
            _data.RefsManifestBLayerArtifactContent,
            ManifestMediaType
        );
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task A12_Setup_PushRefsManifestCLayerArtifact()
    {
        // Subject is manifests[3], which was NOT pushed — tests non-existent subject referrers
        var resp = await TestData.PushManifestAsync(
            _client,
            _ns,
            _data.RefsManifestCLayerArtifactDigest,
            _data.RefsManifestCLayerArtifactContent,
            ManifestMediaType
        );
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    // ── B: Tag Listing ───────────────────────────────────────────────

    [Fact]
    public async Task B1_ListTags_Returns200InSortedOrder()
    {
        var resp = await _client.GetAsync($"/v2/{_ns}/tags/list");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var tags = doc
            .RootElement.GetProperty("tags")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        Assert.NotEmpty(tags);

        // OCI spec allows either lexical (case-insensitive) or ASCIIbetical (ordinal) sort
        var lexicalSorted = tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        var ordinalSorted = tags.OrderBy(t => t, StringComparer.Ordinal).ToList();

        Assert.True(
            tags.SequenceEqual(lexicalSorted) || tags.SequenceEqual(ordinalSorted),
            $"Tags are not in sorted order. Got: [{string.Join(", ", tags)}]"
        );
    }

    [Fact]
    public async Task B2_ListTags_WithN_LimitsResults()
    {
        var resp = await _client.GetAsync($"/v2/{_ns}/tags/list?n=4");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var tags = doc
            .RootElement.GetProperty("tags")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        Assert.Equal(4, tags.Count);
    }

    [Fact]
    public async Task B3_ListTags_WithNAndLast_PaginatesCorrectly()
    {
        // First page
        var resp1 = await _client.GetAsync($"/v2/{_ns}/tags/list?n=4");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        var body1 = await resp1.Content.ReadAsStringAsync();
        using var doc1 = JsonDocument.Parse(body1);
        var page1Tags = doc1
            .RootElement.GetProperty("tags")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        Assert.Equal(4, page1Tags.Count);
        var lastTag = page1Tags.Last();

        // Second page
        var resp2 = await _client.GetAsync(
            $"/v2/{_ns}/tags/list?n=4&last={Uri.EscapeDataString(lastTag)}"
        );
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        var body2 = await resp2.Content.ReadAsStringAsync();
        using var doc2 = JsonDocument.Parse(body2);
        var page2Tags = doc2
            .RootElement.GetProperty("tags")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        // No overlap between pages
        var overlap = page1Tags.Intersect(page2Tags).ToList();
        Assert.Empty(overlap);
    }

    // ── C: Referrers ─────────────────────────────────────────────────

    [Fact]
    public async Task C1_ReferrersForNonexistentDigest_Returns200EmptyIndex()
    {
        var resp = await _client.GetAsync($"/v2/{_ns}/referrers/{_data.DummyDigest}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var contentType = resp.Content.Headers.ContentType?.MediaType;
        Assert.Equal(IndexMediaType, contentType);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var manifests = doc.RootElement.GetProperty("manifests");
        Assert.Equal(0, manifests.GetArrayLength());
    }

    [Fact]
    public async Task C2_ReferrersForExistingManifest_Returns200WithDescriptors()
    {
        var resp = await _client.GetAsync($"/v2/{_ns}/referrers/{_data.Manifests[4].Digest}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var contentType = resp.Content.Headers.ContentType?.MediaType;
        Assert.Equal(IndexMediaType, contentType);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var manifests = doc.RootElement.GetProperty("manifests");
        Assert.Equal(5, manifests.GetArrayLength());

        // Each referrer descriptor should have the test annotation
        foreach (var descriptor in manifests.EnumerateArray())
        {
            if (descriptor.TryGetProperty("annotations", out var annotations))
            {
                Assert.True(annotations.TryGetProperty(_data.TestAnnotationKey, out _));
            }
        }
    }

    [Fact]
    public async Task C3_ReferrersWithArtifactTypeFilter_ReturnsFilteredResults()
    {
        var resp = await _client.GetAsync(
            $"/v2/{_ns}/referrers/{_data.Manifests[4].Digest}?artifactType={Uri.EscapeDataString(_data.TestRefArtifactTypeA)}"
        );
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var manifests = doc.RootElement.GetProperty("manifests");

        if (resp.Headers.TryGetValues("OCI-Filters-Applied", out _))
        {
            // Server applied filtering — expect only artifactTypeA results
            Assert.Equal(2, manifests.GetArrayLength());
        }
        else
        {
            // Server did not filter — full list returned, client must filter
            Assert.Equal(5, manifests.GetArrayLength());
        }
    }

    [Fact]
    public async Task C4_ReferrersForNonExistentSubject_Returns200WithOneReferrer()
    {
        // manifests[3] was never pushed, but RefsManifestCLayerArtifact references it as subject
        var resp = await _client.GetAsync($"/v2/{_ns}/referrers/{_data.Manifests[3].Digest}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var manifests = doc.RootElement.GetProperty("manifests");
        Assert.Equal(1, manifests.GetArrayLength());

        var referrer = manifests[0];
        Assert.Equal(
            _data.RefsManifestCLayerArtifactDigest,
            referrer.GetProperty("digest").GetString()
        );
    }
}

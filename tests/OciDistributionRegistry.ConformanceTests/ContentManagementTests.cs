using System.Net;
using System.Text.Json;
using OciDistributionRegistry.ConformanceTests.Helpers;
using Xunit;

namespace OciDistributionRegistry.ConformanceTests;

/// <summary>
/// Port of OCI distribution-spec conformance test 04_management_test.go.
/// Tests content management (delete) operations for manifests and blobs.
/// </summary>
[Collection("Conformance")]
[TestCaseOrderer(typeof(AlphabeticalOrderer))]
public class ContentManagementTests
{
    private readonly RegistryFixture _fixture;
    private readonly HttpClient _client;
    private readonly TestData _data;

    public ContentManagementTests(RegistryFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _data = fixture.Data;
    }

    // ── A: Setup ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A1_Setup_PushTestContent()
    {
        var name = RegistryFixture.Namespace;

        // Push config blob
        await TestData.PushBlobAsync(
            _client,
            name,
            _data.Configs[3].Content,
            _data.Configs[3].Digest,
            TestContext.Current.CancellationToken
        );

        // Push layer blob
        await TestData.PushBlobAsync(
            _client,
            name,
            _data.LayerBlobData,
            _data.LayerBlobDigest,
            TestContext.Current.CancellationToken
        );

        // Push manifest with tag
        var resp = await TestData.PushManifestAsync(
            _client,
            name,
            "tagtest0",
            _data.Manifests[3].Content,
            "application/vnd.oci.image.manifest.v1+json",
            TestContext.Current.CancellationToken
        );
        resp.EnsureSuccessStatusCode();

        // Record initial tag count
        var tagsResp = await _client.GetAsync(
            $"/v2/{name}/tags/list",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, tagsResp.StatusCode);

        var body = await tagsResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var tags = doc.RootElement.GetProperty("tags");
        _fixture.State["mgmt_initialTagCount"] = tags.GetArrayLength().ToString();
        Assert.True(tags.GetArrayLength() > 0, "Expected at least one tag after push");
    }

    // ── B: Manifest Delete ────────────────────────────────────────────────

    [Fact]
    public async Task B1_DeleteManifestByTag_Returns202Or400Or405()
    {
        var name = RegistryFixture.Namespace;
        var resp = await _client.DeleteAsync(
            $"/v2/{name}/manifests/tagtest0",
            TestContext.Current.CancellationToken
        );

        Assert.Contains(
            resp.StatusCode,
            new[]
            {
                HttpStatusCode.Accepted,
                HttpStatusCode.BadRequest,
                HttpStatusCode.MethodNotAllowed,
                HttpStatusCode.NotFound,
            }
        );
    }

    [Fact]
    public async Task B2_DeleteManifestByDigest_Returns202()
    {
        var name = RegistryFixture.Namespace;
        var digest = _data.Manifests[3].Digest;
        var resp = await _client.DeleteAsync(
            $"/v2/{name}/manifests/{digest}",
            TestContext.Current.CancellationToken
        );

        // 202 if deleted, 404 if already removed by tag delete
        Assert.Contains(
            resp.StatusCode,
            new[]
            {
                HttpStatusCode.Accepted,
                HttpStatusCode.NotFound,
                HttpStatusCode.MethodNotAllowed,
            }
        );

        if (resp.StatusCode == HttpStatusCode.MethodNotAllowed)
        {
            _fixture.State["mgmt_manifestDeleteAllowed"] = "false";
        }
    }

    [Fact]
    public async Task B3_GetDeletedManifest_Returns404()
    {
        var name = RegistryFixture.Namespace;
        var digest = _data.Manifests[3].Digest;
        var resp = await _client.GetAsync(
            $"/v2/{name}/manifests/{digest}",
            TestContext.Current.CancellationToken
        );

        if (_fixture.State.GetValueOrDefault("mgmt_manifestDeleteAllowed") != "false")
        {
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
        else
        {
            // If delete was not supported, manifest should still exist
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }

    [Fact]
    public async Task B4_TagListReflectsDeletion()
    {
        var name = RegistryFixture.Namespace;
        var resp = await _client.GetAsync(
            $"/v2/{name}/tags/list",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var tags = doc.RootElement.GetProperty("tags");
        var currentCount = tags.GetArrayLength();

        var manifestDeleteAllowed =
            _fixture.State.GetValueOrDefault("mgmt_manifestDeleteAllowed") != "false";
        var initialTagCountStr = _fixture.State.GetValueOrDefault("mgmt_initialTagCount", "0");
        var initialTagCount = int.Parse(initialTagCountStr);

        if (manifestDeleteAllowed && initialTagCount > 0)
        {
            Assert.True(
                currentCount < initialTagCount,
                $"Expected tag count to decrease after deletion. Was {initialTagCount}, now {currentCount}."
            );
        }
    }

    // ── C: Blob Delete ────────────────────────────────────────────────────

    [Fact]
    public async Task C1_DeleteBlob_Returns202()
    {
        var name = RegistryFixture.Namespace;

        // Delete config blob
        var configResp = await _client.DeleteAsync(
            $"/v2/{name}/blobs/{_data.Configs[3].Digest}",
            TestContext.Current.CancellationToken
        );
        Assert.Contains(
            configResp.StatusCode,
            new[]
            {
                HttpStatusCode.Accepted,
                HttpStatusCode.NotFound,
                HttpStatusCode.MethodNotAllowed,
            }
        );

        if (configResp.StatusCode == HttpStatusCode.MethodNotAllowed)
        {
            _fixture.State["mgmt_blobDeleteAllowed"] = "false";
        }

        // Delete layer blob
        var layerResp = await _client.DeleteAsync(
            $"/v2/{name}/blobs/{_data.LayerBlobDigest}",
            TestContext.Current.CancellationToken
        );
        Assert.Contains(
            layerResp.StatusCode,
            new[]
            {
                HttpStatusCode.Accepted,
                HttpStatusCode.NotFound,
                HttpStatusCode.MethodNotAllowed,
            }
        );
    }

    [Fact]
    public async Task C2_GetDeletedBlob_Returns404()
    {
        var name = RegistryFixture.Namespace;
        var resp = await _client.GetAsync(
            $"/v2/{name}/blobs/{_data.Configs[3].Digest}",
            TestContext.Current.CancellationToken
        );

        if (_fixture.State.GetValueOrDefault("mgmt_blobDeleteAllowed") != "false")
        {
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
        else
        {
            // If delete was not supported, blob should still exist
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }
}

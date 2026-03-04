using System.Net;
using System.Text.Json;
using OciDistributionRegistry.ConformanceTests.Helpers;
using Xunit;

namespace OciDistributionRegistry.ConformanceTests;

[Collection("Conformance")]
[TestCaseOrderer("OciDistributionRegistry.ConformanceTests.Helpers.AlphabeticalOrderer", "OciDistributionRegistry.ConformanceTests")]
public class PullTests
{
    private readonly RegistryFixture _fixture;
    private HttpClient Client => _fixture.Client;
    private TestData Data => _fixture.Data;
    private string Ns => RegistryFixture.Namespace;

    public PullTests(RegistryFixture fixture) => _fixture = fixture;

    // ── Setup ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A0_Setup_PushContentForPullTests()
    {
        // Push config blobs
        await TestData.PushBlobAsync(Client, Ns, Data.Configs[0].Content, Data.Configs[0].Digest);
        await TestData.PushBlobAsync(Client, Ns, Data.Configs[1].Content, Data.Configs[1].Digest);

        // Push layer blob
        await TestData.PushBlobAsync(Client, Ns, Data.LayerBlobData, Data.LayerBlobDigest);

        // Push manifest[0] with tag
        var resp0 = await TestData.PushManifestAsync(Client, Ns, "tagtest0",
            Data.Manifests[0].Content, "application/vnd.oci.image.manifest.v1+json");
        resp0.EnsureSuccessStatusCode();

        // Push manifest[1] by digest
        var resp1 = await TestData.PushManifestAsync(Client, Ns, Data.Manifests[1].Digest,
            Data.Manifests[1].Content, "application/vnd.oci.image.manifest.v1+json");
        resp1.EnsureSuccessStatusCode();
    }

    // ── Pull Blobs ───────────────────────────────────────────────────────

    [Fact]
    public async Task B0_HeadNonexistentBlob_Returns404()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, $"/v2/{Ns}/blobs/{Data.DummyDigest}");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task B1_HeadExistingBlob_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, $"/v2/{Ns}/blobs/{Data.Configs[0].Digest}");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dcd = response.Headers.GetValues("Docker-Content-Digest").FirstOrDefault();
        Assert.Equal(Data.Configs[0].Digest, dcd);
    }

    [Fact]
    public async Task B2_GetNonexistentBlob_Returns404()
    {
        var response = await Client.GetAsync($"/v2/{Ns}/blobs/{Data.DummyDigest}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task B3_GetExistingBlob_Returns200()
    {
        var response = await Client.GetAsync($"/v2/{Ns}/blobs/{Data.Configs[0].Digest}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Pull Manifests ───────────────────────────────────────────────────

    [Fact]
    public async Task C0_HeadNonexistentManifest_Returns404()
    {
        var request = new HttpRequestMessage(HttpMethod.Head,
            $"/v2/{Ns}/manifests/{Data.NonexistentManifest}");
        var response = await Client.SendAsync(request);
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 404 or 400 but got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task C1_HeadManifestByDigest_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Head,
            $"/v2/{Ns}/manifests/{Data.Manifests[0].Digest}");
        request.Headers.Add("Accept", "application/vnd.oci.image.manifest.v1+json");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dcd = response.Headers.GetValues("Docker-Content-Digest").FirstOrDefault();
        Assert.Equal(Data.Manifests[0].Digest, dcd);
    }

    [Fact]
    public async Task C2_HeadManifestByTag_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Head,
            $"/v2/{Ns}/manifests/tagtest0");
        request.Headers.Add("Accept", "application/vnd.oci.image.manifest.v1+json");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dcd = response.Headers.GetValues("Docker-Content-Digest").FirstOrDefault();
        Assert.Equal(Data.Manifests[0].Digest, dcd);
    }

    [Fact]
    public async Task C3_GetNonexistentManifest_Returns404()
    {
        var response = await Client.GetAsync($"/v2/{Ns}/manifests/{Data.NonexistentManifest}");
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 404 or 400 but got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task C4_GetManifestByDigest_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/v2/{Ns}/manifests/{Data.Manifests[0].Digest}");
        request.Headers.Add("Accept", "application/vnd.oci.image.manifest.v1+json");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task C5_GetManifestByTag_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/v2/{Ns}/manifests/tagtest0");
        request.Headers.Add("Accept", "application/vnd.oci.image.manifest.v1+json");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Error Codes ──────────────────────────────────────────────────────

    [Fact]
    public async Task D0_GetInvalidDigestManifest_Returns400Or404WithErrorJson()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/v2/{Ns}/manifests/sha256:totallywrong");
        request.Headers.Add("Accept", "application/vnd.oci.image.manifest.v1+json");
        var response = await Client.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 400 or 404 but got {(int)response.StatusCode}");

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            var errors = doc.RootElement.GetProperty("errors");
            Assert.True(errors.GetArrayLength() > 0, "Expected at least one error entry");

            var code = errors[0].GetProperty("code").GetString();
            Assert.False(string.IsNullOrEmpty(code), "Error code must not be empty");
        }
    }
}

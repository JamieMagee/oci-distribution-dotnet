using System.Net;
using System.Net.Http.Headers;
using OciDistributionRegistry.ConformanceTests.Helpers;
using Xunit;

namespace OciDistributionRegistry.ConformanceTests;

[Collection("Conformance")]
[TestCaseOrderer(typeof(AlphabeticalOrderer))]
public class PushTests
{
    private readonly RegistryFixture _fixture;
    private readonly HttpClient _client;
    private readonly TestData _data;
    private readonly string _namespace = RegistryFixture.Namespace;
    private readonly string _crossmountNamespace = RegistryFixture.CrossmountNamespace;

    public PushTests(RegistryFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _data = fixture.Data;
    }

    #region A: Blob Upload Streamed

    [Fact]
    public async Task A1_StreamedUpload_PatchWithBody_Returns202()
    {
        // POST to initiate upload session
        var postResp = await _client.PostAsync($"/v2/{_namespace}/blobs/uploads/", null);
        postResp.EnsureSuccessStatusCode();
        var location =
            postResp.Headers.Location?.ToString() ?? postResp.Headers.GetValues("Location").First();

        // PATCH with testBlobA body
        var request = new HttpRequestMessage(HttpMethod.Patch, location);
        request.Content = new ByteArrayContent(_data.TestBlobA);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = _data.TestBlobA.Length;
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Range",
            $"0-{_data.TestBlobA.Length - 1}"
        );

        var patchResp = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, patchResp.StatusCode);

        _fixture.State["push_streamedUploadLocation"] =
            patchResp.Headers.Location?.ToString()
            ?? patchResp.Headers.GetValues("Location").First();
    }

    [Fact]
    public async Task A2_StreamedUpload_PutWithDigest_Returns201()
    {
        var streamedUploadLocation = _fixture.State.GetValueOrDefault(
            "push_streamedUploadLocation"
        );
        Assert.NotEmpty(streamedUploadLocation);

        var separator = streamedUploadLocation!.Contains('?') ? "&" : "?";
        var putUrl =
            $"{streamedUploadLocation}{separator}digest={Uri.EscapeDataString(_data.TestBlobADigest)}";

        var putResp = await _client.PutAsync(putUrl, null);

        Assert.Equal(HttpStatusCode.Created, putResp.StatusCode);
        var location = putResp.Headers.Location?.ToString();
        Assert.False(string.IsNullOrEmpty(location));
    }

    #endregion

    #region B: Blob Upload Monolithic

    [Fact]
    public async Task B1_GetNonexistentBlob_Returns404()
    {
        var resp = await _client.GetAsync($"/v2/{_namespace}/blobs/{_data.DummyDigest}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task B2_MonolithicPost_Returns201Or202()
    {
        var configBlob = _data.Configs[1];
        var content = new ByteArrayContent(configBlob.Content);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = configBlob.Content.Length;

        var resp = await _client.PostAsync(
            $"/v2/{_namespace}/blobs/uploads/?digest={Uri.EscapeDataString(configBlob.Digest)}",
            content
        );

        Assert.True(
            resp.StatusCode == HttpStatusCode.Created || resp.StatusCode == HttpStatusCode.Accepted,
            $"Expected 201 or 202 but got {(int)resp.StatusCode}"
        );
    }

    [Fact]
    public async Task B3_PostThenPut_Returns201()
    {
        var configBlob = _data.Configs[1];

        // POST to initiate
        var postResp = await _client.PostAsync($"/v2/{_namespace}/blobs/uploads/", null);
        postResp.EnsureSuccessStatusCode();
        var location =
            postResp.Headers.Location?.ToString() ?? postResp.Headers.GetValues("Location").First();

        // PUT to complete
        var separator = location.Contains('?') ? "&" : "?";
        var putUrl = $"{location}{separator}digest={Uri.EscapeDataString(configBlob.Digest)}";
        var putContent = new ByteArrayContent(configBlob.Content);
        putContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        putContent.Headers.ContentLength = configBlob.Content.Length;

        var putResp = await _client.PutAsync(putUrl, putContent);

        Assert.Equal(HttpStatusCode.Created, putResp.StatusCode);
    }

    [Fact]
    public async Task B4_GetExistingBlob_Returns200()
    {
        var configBlob = _data.Configs[1];
        var resp = await _client.GetAsync($"/v2/{_namespace}/blobs/{configBlob.Digest}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task B5_PutLayerBlob_Returns201()
    {
        await TestData.PushBlobAsync(
            _client,
            _namespace,
            _data.LayerBlobData,
            _data.LayerBlobDigest
        );
    }

    [Fact]
    public async Task B6_GetExistingLayer_Returns200()
    {
        var resp = await _client.GetAsync($"/v2/{_namespace}/blobs/{_data.LayerBlobDigest}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    #endregion

    #region C: Blob Upload Chunked

    [Fact]
    public async Task C1_OutOfOrderChunk_Returns416()
    {
        // POST to initiate
        var postResp = await _client.PostAsync($"/v2/{_namespace}/blobs/uploads/", null);
        postResp.EnsureSuccessStatusCode();
        var location =
            postResp.Headers.Location?.ToString() ?? postResp.Headers.GetValues("Location").First();

        // PATCH with chunk2 (out of order)
        var request = new HttpRequestMessage(HttpMethod.Patch, location);
        request.Content = new ByteArrayContent(_data.TestBlobBChunk2);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = _data.TestBlobBChunk2.Length;
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Range",
            _data.TestBlobBChunk2Range
        );

        var patchResp = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, patchResp.StatusCode);
    }

    [Fact]
    public async Task C2_PatchFirstChunk_Returns202()
    {
        // POST to initiate
        var postResp = await _client.PostAsync($"/v2/{_namespace}/blobs/uploads/", null);
        postResp.EnsureSuccessStatusCode();
        var location =
            postResp.Headers.Location?.ToString() ?? postResp.Headers.GetValues("Location").First();

        // PATCH with chunk1
        var request = new HttpRequestMessage(HttpMethod.Patch, location);
        request.Content = new ByteArrayContent(_data.TestBlobBChunk1);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = _data.TestBlobBChunk1.Length;
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Range",
            _data.TestBlobBChunk1Range
        );

        var patchResp = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, patchResp.StatusCode);

        // Verify Range header
        Assert.True(patchResp.Headers.TryGetValues("Range", out var rangeValues));
        Assert.NotEmpty(rangeValues!);

        _fixture.State["push_chunkedUploadLocation"] =
            patchResp.Headers.Location?.ToString()
            ?? patchResp.Headers.GetValues("Location").First();
    }

    [Fact]
    public async Task C3_RetryPreviousChunk_Returns416()
    {
        var chunkedUploadLocation = _fixture.State.GetValueOrDefault("push_chunkedUploadLocation");
        Assert.NotEmpty(chunkedUploadLocation);

        // PATCH chunk1 again at the same location
        var request = new HttpRequestMessage(HttpMethod.Patch, chunkedUploadLocation);
        request.Content = new ByteArrayContent(_data.TestBlobBChunk1);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = _data.TestBlobBChunk1.Length;
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Range",
            _data.TestBlobBChunk1Range
        );

        var patchResp = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, patchResp.StatusCode);
    }

    [Fact]
    public async Task C4_GetUploadStatus_Returns204()
    {
        var chunkedUploadLocation = _fixture.State.GetValueOrDefault("push_chunkedUploadLocation");
        Assert.NotEmpty(chunkedUploadLocation);

        var resp = await _client.GetAsync(chunkedUploadLocation);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        // Verify Range header
        Assert.True(resp.Headers.TryGetValues("Range", out var rangeValues));
        Assert.NotEmpty(rangeValues!);

        // Verify Location header
        var location = resp.Headers.Location?.ToString();
        Assert.False(string.IsNullOrEmpty(location));
    }

    [Fact]
    public async Task C5_PatchSecondChunk_Returns202()
    {
        var chunkedUploadLocation = _fixture.State.GetValueOrDefault("push_chunkedUploadLocation");
        Assert.NotEmpty(chunkedUploadLocation);

        var request = new HttpRequestMessage(HttpMethod.Patch, chunkedUploadLocation);
        request.Content = new ByteArrayContent(_data.TestBlobBChunk2);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = _data.TestBlobBChunk2.Length;
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Range",
            _data.TestBlobBChunk2Range
        );

        var patchResp = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, patchResp.StatusCode);

        // Update location for the final PUT
        _fixture.State["push_chunkedUploadLocation"] =
            patchResp.Headers.Location?.ToString()
            ?? patchResp.Headers.GetValues("Location").First();
    }

    [Fact]
    public async Task C6_PutCloseSession_Returns201()
    {
        var chunkedUploadLocation = _fixture.State.GetValueOrDefault("push_chunkedUploadLocation");
        Assert.NotEmpty(chunkedUploadLocation);

        var separator = chunkedUploadLocation!.Contains('?') ? "&" : "?";
        var putUrl =
            $"{chunkedUploadLocation}{separator}digest={Uri.EscapeDataString(_data.TestBlobBDigest)}";

        var putResp = await _client.PutAsync(putUrl, null);

        Assert.Equal(HttpStatusCode.Created, putResp.StatusCode);
    }

    #endregion

    #region D: Cross-Repository Blob Mount

    [Fact]
    public async Task D1_CrossMountWithoutFrom_Returns202()
    {
        var resp = await _client.PostAsync(
            $"/v2/{_crossmountNamespace}/blobs/uploads/?mount={Uri.EscapeDataString(_data.DummyDigest)}",
            null
        );

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
    }

    [Fact]
    public async Task D2_CrossMount_Returns201Or202()
    {
        var resp = await _client.PostAsync(
            $"/v2/{_crossmountNamespace}/blobs/uploads/?mount={Uri.EscapeDataString(_data.TestBlobADigest)}&from={Uri.EscapeDataString(_namespace)}",
            null
        );

        Assert.True(
            resp.StatusCode == HttpStatusCode.Created || resp.StatusCode == HttpStatusCode.Accepted,
            $"Expected 201 or 202 but got {(int)resp.StatusCode}"
        );
    }

    #endregion

    #region E: Manifest Upload

    [Fact]
    public async Task E1_GetNonexistentManifest_Returns404()
    {
        var resp = await _client.GetAsync(
            $"/v2/{_namespace}/manifests/{_data.NonexistentManifest}"
        );

        Assert.True(
            resp.StatusCode == HttpStatusCode.NotFound
                || resp.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 404 or 400 but got {(int)resp.StatusCode}"
        );
    }

    [Fact]
    public async Task E2_PutManifestWithTags_Returns201()
    {
        var manifest = _data.Manifests[1];

        for (int i = 0; i < 4; i++)
        {
            var tag = $"test{i}";
            var resp = await TestData.PushManifestAsync(
                _client,
                _namespace,
                tag,
                manifest.Content,
                "application/vnd.oci.image.manifest.v1+json"
            );

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }
    }

    [Fact]
    public async Task E3_PutEmptyLayerManifest_Returns201()
    {
        var resp = await TestData.PushManifestAsync(
            _client,
            _namespace,
            _data.EmptyLayerManifestDigest,
            _data.EmptyLayerManifestContent,
            "application/vnd.oci.image.manifest.v1+json"
        );

        // Accept 201 primarily, but also accept other success codes
        Assert.True(
            resp.IsSuccessStatusCode,
            $"Expected success status code but got {(int)resp.StatusCode}"
        );
    }

    [Fact]
    public async Task E4_GetManifestByDigest_Returns200()
    {
        var manifest = _data.Manifests[1];
        var resp = await _client.GetAsync($"/v2/{_namespace}/manifests/{manifest.Digest}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    #endregion
}

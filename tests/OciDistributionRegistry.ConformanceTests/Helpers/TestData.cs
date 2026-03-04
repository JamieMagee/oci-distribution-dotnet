using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OciDistributionRegistry.ConformanceTests.Helpers;

/// <summary>
/// Port of setup.go — generates all test blobs, configs, manifests, and referrer artifacts
/// used across the conformance test suite.
/// </summary>
public class TestData
{
    // Base64-encoded tar.gz layer (same as Go conformance suite)
    private const string LayerBase64String =
        "H4sIAAAAAAAAA+3OQQrCMBCF4a49xXgBSUnaHMCTRBptQRNpp6i3t0UEV7oqIv7fYgbmzeJpHHSjVy0" +
        "WZCa1c/MufWVe94N3RWlrZ72x3k/30nhbFWKWLPU0Dhp6keJ8im//PuU/6pZH2WVtYx8b0Sz7LjWSR5VLG6YRBumSzOlGtjkd+qD" +
        "jMWiX07Befbs7AAAAAAAAAAAAAAAAAPyzO34MnqoAKAAA";

    public byte[] LayerBlobData { get; }
    public string LayerBlobDigest { get; }
    public string LayerBlobContentLength { get; }

    /// <summary>Test configs — one per workflow (pull=0, push=1, discovery=2, management=3, refs=4).</summary>
    public TestBlob[] Configs { get; }

    /// <summary>Test manifests — one per workflow, each referencing its own config and the shared layer.</summary>
    public TestBlob[] Manifests { get; }

    // Empty-layer manifest (for push tests)
    public byte[] EmptyLayerManifestContent { get; }
    public string EmptyLayerManifestDigest { get; }

    // Referrer test data
    public byte[] EmptyJsonBlob { get; } = "{}"u8.ToArray();
    public string EmptyJsonBlobDigest { get; }

    public byte[] TestRefBlobA { get; } = "NHL Peanut Butter on my NHL bagel"u8.ToArray();
    public string TestRefBlobADigest { get; }
    public string TestRefArtifactTypeA { get; } = "application/vnd.nhl.peanut.butter.bagel";

    public byte[] TestRefBlobB { get; } = "NBA Strawberry Jam on my NBA croissant"u8.ToArray();
    public string TestRefBlobBDigest { get; }
    public string TestRefArtifactTypeB { get; } = "application/vnd.nba.strawberry.jam.croissant";

    public string TestAnnotationKey { get; } = "org.opencontainers.conformance.test";

    // Referrer manifests
    public byte[] RefsManifestAConfigArtifactContent { get; }
    public string RefsManifestAConfigArtifactDigest { get; }

    public byte[] RefsManifestBConfigArtifactContent { get; }
    public string RefsManifestBConfigArtifactDigest { get; }

    public byte[] RefsManifestALayerArtifactContent { get; }
    public string RefsManifestALayerArtifactDigest { get; }

    public byte[] RefsManifestBLayerArtifactContent { get; }
    public string RefsManifestBLayerArtifactDigest { get; }

    public byte[] RefsManifestCLayerArtifactContent { get; }
    public string RefsManifestCLayerArtifactDigest { get; }

    public string TestRefArtifactTypeIndex { get; } = "application/vnd.food.stand";
    public byte[] RefsIndexArtifactContent { get; }
    public string RefsIndexArtifactDigest { get; }

    // Chunked blob data
    public byte[] TestBlobA { get; }
    public string TestBlobADigest { get; }
    public string TestBlobALength { get; }

    public byte[] TestBlobB { get; }
    public string TestBlobBDigest { get; }
    public byte[] TestBlobBChunk1 { get; }
    public string TestBlobBChunk1Length { get; }
    public string TestBlobBChunk1Range { get; }
    public byte[] TestBlobBChunk2 { get; }
    public string TestBlobBChunk2Length { get; }
    public string TestBlobBChunk2Range { get; }

    public string DummyDigest { get; }

    public string NonexistentManifest { get; } = ".INVALID_MANIFEST_NAME";

    public TestData()
    {
        // Layer blob
        LayerBlobData = Convert.FromBase64String(LayerBase64String);
        LayerBlobDigest = ComputeDigest(LayerBlobData);
        LayerBlobContentLength = LayerBlobData.Length.ToString();

        EmptyJsonBlobDigest = ComputeDigest(EmptyJsonBlob);
        TestRefBlobADigest = ComputeDigest(TestRefBlobA);
        TestRefBlobBDigest = ComputeDigest(TestRefBlobB);
        DummyDigest = ComputeDigest("hello world"u8.ToArray());

        // Generate configs (5 workflows: pull, push, discovery, management, refs)
        Configs = new TestBlob[5];
        for (int i = 0; i < 5; i++)
        {
            var config = new
            {
                architecture = "amd64",
                os = "linux",
                rootfs = new { type = "layers", diff_ids = Array.Empty<string>() },
                author = $"conformance-test-{i}-{Guid.NewGuid():N}"
            };
            var configBytes = JsonSerializer.SerializeToUtf8Bytes(config, new JsonSerializerOptions { WriteIndented = true });
            Configs[i] = new TestBlob(configBytes, ComputeDigest(configBytes));
        }

        // Generate manifests
        Manifests = new TestBlob[5];
        for (int i = 0; i < 5; i++)
        {
            var manifest = new ManifestJson
            {
                SchemaVersion = 2,
                MediaType = "application/vnd.oci.image.manifest.v1+json",
                Config = new DescriptorJson
                {
                    MediaType = "application/vnd.oci.image.config.v1+json",
                    Digest = Configs[i].Digest,
                    Size = Configs[i].Content.Length
                },
                Layers = new[]
                {
                    new DescriptorJson
                    {
                        MediaType = "application/vnd.oci.image.layer.v1.tar+gzip",
                        Digest = LayerBlobDigest,
                        Size = LayerBlobData.Length
                    }
                }
            };
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
            Manifests[i] = new TestBlob(manifestBytes, ComputeDigest(manifestBytes));
        }

        // Empty-layer manifest
        var emptyLayerManifest = new ManifestJson
        {
            SchemaVersion = 2,
            Config = new DescriptorJson
            {
                MediaType = "application/vnd.oci.image.config.v1+json",
                Digest = Configs[1].Digest,
                Size = Configs[1].Content.Length
            },
            Layers = Array.Empty<DescriptorJson>()
        };
        EmptyLayerManifestContent = JsonSerializer.SerializeToUtf8Bytes(emptyLayerManifest, new JsonSerializerOptions { WriteIndented = true });
        EmptyLayerManifestDigest = ComputeDigest(EmptyLayerManifestContent);

        // TestBlobA — random blob for streamed upload
        TestBlobA = RandomBlob(42);
        TestBlobADigest = ComputeDigest(TestBlobA);
        TestBlobALength = TestBlobA.Length.ToString();

        // TestBlobB — chunked blob
        TestBlobB = RandomBlob(42);
        TestBlobBDigest = ComputeDigest(TestBlobB);
        var mid = TestBlobB.Length / 2;
        TestBlobBChunk1 = TestBlobB[..mid];
        TestBlobBChunk1Length = TestBlobBChunk1.Length.ToString();
        TestBlobBChunk1Range = $"0-{mid - 1}";
        TestBlobBChunk2 = TestBlobB[mid..];
        TestBlobBChunk2Length = TestBlobBChunk2.Length.ToString();
        TestBlobBChunk2Range = $"{mid}-{TestBlobB.Length - 1}";

        // Referrer artifact manifests
        var emptyJsonDesc = new DescriptorJson
        {
            MediaType = "application/vnd.oci.empty.v1+json",
            Size = EmptyJsonBlob.Length,
            Digest = EmptyJsonBlobDigest
        };

        var subjectDescManifest4 = new DescriptorJson
        {
            MediaType = "application/vnd.oci.image.manifest.v1+json",
            Size = Manifests[4].Content.Length,
            Digest = Manifests[4].Digest
        };

        var subjectDescManifest3 = new DescriptorJson
        {
            MediaType = "application/vnd.oci.image.manifest.v1+json",
            Size = Manifests[3].Content.Length,
            Digest = Manifests[3].Digest
        };

        // A: config.MediaType = artifactType
        var refsManifestAConfig = new ManifestJson
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.manifest.v1+json",
            Config = new DescriptorJson { MediaType = TestRefArtifactTypeA, Size = TestRefBlobA.Length, Digest = TestRefBlobADigest },
            Subject = subjectDescManifest4,
            Layers = new[] { emptyJsonDesc },
            Annotations = new Dictionary<string, string> { [TestAnnotationKey] = "test config a" }
        };
        RefsManifestAConfigArtifactContent = JsonSerializer.SerializeToUtf8Bytes(refsManifestAConfig, new JsonSerializerOptions { WriteIndented = true });
        RefsManifestAConfigArtifactDigest = ComputeDigest(RefsManifestAConfigArtifactContent);

        // B: config.MediaType = artifactType (different type)
        var refsManifestBConfig = new ManifestJson
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.manifest.v1+json",
            Config = new DescriptorJson { MediaType = TestRefArtifactTypeB, Size = TestRefBlobB.Length, Digest = TestRefBlobBDigest },
            Subject = subjectDescManifest4,
            Layers = new[] { emptyJsonDesc },
            Annotations = new Dictionary<string, string> { [TestAnnotationKey] = "test config b" }
        };
        RefsManifestBConfigArtifactContent = JsonSerializer.SerializeToUtf8Bytes(refsManifestBConfig, new JsonSerializerOptions { WriteIndented = true });
        RefsManifestBConfigArtifactDigest = ComputeDigest(RefsManifestBConfigArtifactContent);

        // A: ArtifactType field, config = emptyJSON
        var refsManifestALayer = new ManifestJson
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.manifest.v1+json",
            ArtifactType = TestRefArtifactTypeA,
            Config = emptyJsonDesc,
            Subject = subjectDescManifest4,
            Layers = new[] { new DescriptorJson { MediaType = TestRefArtifactTypeA, Size = TestRefBlobA.Length, Digest = TestRefBlobADigest } },
            Annotations = new Dictionary<string, string> { [TestAnnotationKey] = "test layer a" }
        };
        RefsManifestALayerArtifactContent = JsonSerializer.SerializeToUtf8Bytes(refsManifestALayer, new JsonSerializerOptions { WriteIndented = true });
        RefsManifestALayerArtifactDigest = ComputeDigest(RefsManifestALayerArtifactContent);

        // B: ArtifactType field, config = emptyJSON (different type)
        var refsManifestBLayer = new ManifestJson
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.manifest.v1+json",
            ArtifactType = TestRefArtifactTypeB,
            Config = emptyJsonDesc,
            Subject = subjectDescManifest4,
            Layers = new[] { new DescriptorJson { MediaType = TestRefArtifactTypeB, Size = TestRefBlobB.Length, Digest = TestRefBlobBDigest } },
            Annotations = new Dictionary<string, string> { [TestAnnotationKey] = "test layer b" }
        };
        RefsManifestBLayerArtifactContent = JsonSerializer.SerializeToUtf8Bytes(refsManifestBLayer, new JsonSerializerOptions { WriteIndented = true });
        RefsManifestBLayerArtifactDigest = ComputeDigest(RefsManifestBLayerArtifactContent);

        // C: Same as B but subject = manifests[3] (non-existent subject)
        var refsManifestCLayer = new ManifestJson
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.manifest.v1+json",
            ArtifactType = TestRefArtifactTypeB,
            Config = emptyJsonDesc,
            Subject = subjectDescManifest3,
            Layers = new[] { new DescriptorJson { MediaType = TestRefArtifactTypeB, Size = TestRefBlobB.Length, Digest = TestRefBlobBDigest } }
        };
        RefsManifestCLayerArtifactContent = JsonSerializer.SerializeToUtf8Bytes(refsManifestCLayer, new JsonSerializerOptions { WriteIndented = true });
        RefsManifestCLayerArtifactDigest = ComputeDigest(RefsManifestCLayerArtifactContent);

        // Image index artifact
        var refsIndexArtifact = new IndexJson
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.index.v1+json",
            ArtifactType = TestRefArtifactTypeIndex,
            Manifests = new[]
            {
                new DescriptorJson { MediaType = "application/vnd.oci.image.manifest.v1+json", Size = RefsManifestAConfigArtifactContent.Length, Digest = RefsManifestAConfigArtifactDigest },
                new DescriptorJson { MediaType = "application/vnd.oci.image.manifest.v1+json", Size = RefsManifestALayerArtifactContent.Length, Digest = RefsManifestALayerArtifactDigest }
            },
            Subject = subjectDescManifest4,
            Annotations = new Dictionary<string, string> { [TestAnnotationKey] = "test index" }
        };
        RefsIndexArtifactContent = JsonSerializer.SerializeToUtf8Bytes(refsIndexArtifact, new JsonSerializerOptions { WriteIndented = true });
        RefsIndexArtifactDigest = ComputeDigest(RefsIndexArtifactContent);
    }

    public static string ComputeDigest(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static byte[] RandomBlob(int size)
    {
        var blob = new byte[size];
        RandomNumberGenerator.Fill(blob);
        return blob;
    }

    /// <summary>
    /// Helper to push a blob via POST (initiate) → PUT (complete) to the given client.
    /// Returns the blob location.
    /// </summary>
    public static async Task<string> PushBlobAsync(HttpClient client, string name, byte[] data, string digest)
    {
        // Check if blob already exists
        var headResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/v2/{name}/blobs/{digest}"));
        if (headResp.StatusCode == System.Net.HttpStatusCode.OK)
        {
            return $"/v2/{name}/blobs/{digest}";
        }

        // POST to initiate
        var postResp = await client.PostAsync($"/v2/{name}/blobs/uploads/", null);
        postResp.EnsureSuccessStatusCode();
        var location = postResp.Headers.Location?.ToString()
            ?? postResp.Headers.GetValues("Location").First();

        // PUT to complete
        var separator = location.Contains('?') ? "&" : "?";
        var putUrl = $"{location}{separator}digest={Uri.EscapeDataString(digest)}";
        var putContent = new ByteArrayContent(data);
        putContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        putContent.Headers.ContentLength = data.Length;
        var putResp = await client.PutAsync(putUrl, putContent);
        putResp.EnsureSuccessStatusCode();

        return putResp.Headers.Location?.ToString()
            ?? putResp.Headers.GetValues("Location").First();
    }

    /// <summary>
    /// Helper to push a manifest via PUT.
    /// </summary>
    public static async Task<HttpResponseMessage> PushManifestAsync(HttpClient client, string name, string reference, byte[] content, string mediaType)
    {
        var putContent = new ByteArrayContent(content);
        putContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        return await client.PutAsync($"/v2/{name}/manifests/{reference}", putContent);
    }
}

public record TestBlob(byte[] Content, string Digest)
{
    public string ContentLength => Content.Length.ToString();
}

#region JSON models for test data serialization

internal class ManifestJson
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("mediaType")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MediaType { get; set; }
    [JsonPropertyName("artifactType")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArtifactType { get; set; }
    [JsonPropertyName("config")] public required DescriptorJson Config { get; set; }
    [JsonPropertyName("layers")] public required DescriptorJson[] Layers { get; set; }
    [JsonPropertyName("subject")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DescriptorJson? Subject { get; set; }
    [JsonPropertyName("annotations")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Annotations { get; set; }
}

internal class IndexJson
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("mediaType")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MediaType { get; set; }
    [JsonPropertyName("artifactType")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArtifactType { get; set; }
    [JsonPropertyName("manifests")] public required DescriptorJson[] Manifests { get; set; }
    [JsonPropertyName("subject")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DescriptorJson? Subject { get; set; }
    [JsonPropertyName("annotations")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Annotations { get; set; }
}

internal class DescriptorJson
{
    [JsonPropertyName("mediaType")] public required string MediaType { get; set; }
    [JsonPropertyName("digest")] public required string Digest { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("annotations")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Annotations { get; set; }
}

#endregion

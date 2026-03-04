using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace OciDistributionRegistry.ConformanceTests.Helpers;

/// <summary>
/// Shared test fixture that provides a WebApplicationFactory-backed HttpClient
/// with isolated temp storage for each test run.
/// </summary>
public class RegistryFixture : IDisposable
{
    public HttpClient Client { get; }
    public WebApplicationFactory<Program> Factory { get; }
    public string StoragePath { get; }
    public TestData Data { get; }
    public ConcurrentDictionary<string, string> State { get; } = new();

    public const string Namespace = "conformancetest";
    public const string CrossmountNamespace = "conformanceother";

    public RegistryFixture()
    {
        StoragePath = Path.Combine(Path.GetTempPath(), $"oci-conformance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(StoragePath);

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Storage:Path"] = StoragePath
                    });
                });
            });

        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        Data = new TestData();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        try
        {
            if (Directory.Exists(StoragePath))
                Directory.Delete(StoragePath, recursive: true);
        }
        catch { /* best effort cleanup */ }
    }
}

/// <summary>
/// Collection definition so all test classes share the same RegistryFixture instance.
/// </summary>
[CollectionDefinition("Conformance")]
public class ConformanceCollection : ICollectionFixture<RegistryFixture>
{
}

using OciDistributionRegistry.Middleware;
using OciDistributionRegistry.Repositories;
using OciDistributionRegistry.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/oci-registry-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = false;
    });

// Add OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Add health checks
builder
    .Services.AddHealthChecks()
    .AddCheck(
        "storage",
        () =>
        {
            var storagePath =
                builder.Configuration.GetValue<string>("Storage:Path") ?? "/tmp/oci-registry";
            return Directory.Exists(storagePath)
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                    "Storage directory accessible"
                )
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                    "Storage directory not accessible"
                );
        }
    );

// Register repositories
builder.Services.AddSingleton<IBlobRepository, FileSystemBlobRepository>();
builder.Services.AddSingleton<IManifestRepository, FileSystemManifestRepository>();

// Register services
builder.Services.AddScoped<IValidationService, ValidationService>();

// Add memory cache for performance
builder.Services.AddMemoryCache();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders(
                "Docker-Content-Digest",
                "Location",
                "Range",
                "Content-Length",
                "OCI-Subject",
                "OCI-Filters-Applied"
            );
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Use custom middleware
app.UseOciErrorHandling();
app.UseDockerCompatibility();

app.UseCors();

app.UseRouting();

// Add health check endpoint
app.MapHealthChecks("/health");

app.MapControllers();

// Log startup information
Log.Information("OCI Distribution Registry starting up...");
Log.Information(
    "Storage path: {StoragePath}",
    builder.Configuration.GetValue<string>("Storage:Path") ?? "/tmp/oci-registry"
);

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

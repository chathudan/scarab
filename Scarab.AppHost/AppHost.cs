using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis")
    .WithDataVolume();

var resultsDb = builder.AddPostgres("results-pg")
    .WithDataVolume()
    .AddDatabase("results-db", databaseName: "beetle_results");

var scarab = builder.AddProject<Projects.Scarab>("scarab")
    // Scarab has no launchSettings.json - it picks its own listen port from
    // App:Server (appsettings.json, default 6060) via an explicit UseUrls() call in
    // Program.cs rather than reading Aspire's injected ASPNETCORE_URLS. Without this,
    // Aspire has no endpoint to register, so the dashboard shows no host/port at all.
    // isProxied: false because the port is fixed and not something Aspire can reassign.
    .WithHttpEndpoint(port: 6060, targetPort: 6060, name: "http", isProxied: false)
    .WithReference(redis)
    .WithReference(resultsDb)
    .WaitFor(redis)
    .WaitFor(resultsDb)
    .WithEnvironment("SCARAB__Results__pg_results__Type", "postgres")
    .WithEnvironment("SCARAB__Results__pg_results__MaxActive", "50")
    .WithUrlForEndpoint("http", endpoint => new ResourceUrlAnnotation
    {
        Url = $"{endpoint.Url}/scalar/v1",
        DisplayText = "API Docs (Scalar)"
    });

// Seeded demo source database (same data as docker-compose's `source-db` service) - dev/demo
// only, so it isn't provisioned when the AppHost is run outside Development.
if (builder.Environment.IsDevelopment())
{
    var sourceDb = builder.AddMySql("source-mysql")
        .WithDataVolume()
        .WithBindMount("../db/mysql-init", "/docker-entrypoint-initdb.d")
        .AddDatabase("demo-db", databaseName: "beetle_demo");

    scarab
        .WithReference(sourceDb)
        .WaitFor(sourceDb)
        .WithEnvironment("SCARAB__Db__mysql_demo__Type", "mysql")
        // Missing/unreachable is fine - Scarab skips it at startup instead of crashing.
        .WithEnvironment("SCARAB__Db__mysql_demo__Optional", "true");
}

builder.Build().Run();

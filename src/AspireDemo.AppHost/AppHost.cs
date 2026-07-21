using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Stands in for the third-party API that AspireDemo.Api talks to. No static mapping files -
// stubs are configured at runtime via the WireMock admin API (see the integration tests).
var wiremock = builder.AddWireMock("wiremock");

// Container parity is the default: `aspire run` runs the API as the exact Linux image that
// ships, built by the .NET SDK container publish - no Dockerfile (see README for the publish
// command). Set RUN_API_AS_PROJECT=true for a faster inner loop (process, hot reload) instead.
var runApiAsProject = builder.Configuration.GetValue<bool>("RUN_API_AS_PROJECT");

if (runApiAsProject)
{
    builder.AddProject<Projects.AspireDemo_Api>("api")
        .WithReference(wiremock)
        .WaitFor(wiremock);
}
else
{
    builder.AddContainer("api", "aspiredemo-api", "latest")
        .WithHttpEndpoint(targetPort: 8080)
        .WithReference(wiremock)
        .WaitFor(wiremock);
}

builder.Build().Run();

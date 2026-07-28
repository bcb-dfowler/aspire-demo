using AspireDemo.PaymentService;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// This service is reached over gRPC (via Dapr gRPC proxying), so its app channel must speak
// HTTP/2. The container endpoint is plaintext, so serve h2c: Kestrel can't multiplex HTTP/1.1 and
// HTTP/2 on one plaintext port, so the app port is HTTP/2-only. Dapr health-checks the app over
// its own channel, and nothing here depends on the HTTP/1.1 health endpoints being reachable.
builder.WebHost.ConfigureKestrel(options =>
    options.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http2));

builder.Services.AddGrpc();

builder.Services.AddDaprClient();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<PaymentGrpcService>();

app.Run();

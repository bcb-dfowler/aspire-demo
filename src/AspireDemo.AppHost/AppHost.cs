using Aspire.Hosting.ApplicationModel;

// Pin every daprio/* image to one matching runtime tag, and keep it in step with the Dapr SDK
// version referenced by the .NET projects (Dapr.AspNetCore/Dapr.Workflow/Dapr.Messaging/Dapr.Client).
const string DaprRuntimeTag = "1.18.2";

var builder = DistributedApplication.CreateBuilder(args);

// Stands in for the third-party carrier API that order-svc's workflow calls for a shipping
// quote. No static mapping files - stubs are configured at runtime via the WireMock admin API
// (see the integration tests).
var wiremock = builder.AddWireMock("wiremock");

// State (order/cart state, inventory stock, the actor state store backing Dapr Workflow) and
// pub/sub both live in Redis. No password - this is local-only infrastructure, and it lets the
// Dapr component yaml (which can't read Aspire's generated secrets) stay static.
var redis = builder.AddRedis("redis").WithPassword(null);

// order-svc streams order events here (bindings.kafka output); analytics-svc consumes them
// (bindings.kafka input).
var kafka = builder.AddKafka("kafka");

// Azurite - stands in for Azure Blob Storage. order-svc writes invoices here
// (bindings.azure.blobstorage output).
var storage = builder.AddAzureStorage("storage").RunAsEmulator();

// Dapr's actor placement service - required because Dapr Workflow uses actors internally.
var placement = builder.AddContainer("placement", "daprio/placement", DaprRuntimeTag)
    .WithArgs("./placement", "--port", "50006");

// Dapr's workflow scheduler (reminders), Dapr >= 1.14. Needs a persistent-looking data dir for
// its embedded etcd store, even for this single-instance local setup.
var scheduler = builder.AddContainer("scheduler", "daprio/scheduler", DaprRuntimeTag)
    .WithArgs("./scheduler", "--port", "50007", "--etcd-data-dir", "/data")
    .WithContainerRuntimeArgs("--user", "root")
    .WithVolume("dapr-scheduler-data", "/data");

// order-svc (AspireDemo.Api): front door (POST /orders), hosts the fulfillment workflow.
var orderSvcDapr = AddDaprSidecar("api", appPort: 8080, extraComponentsDir: "order-svc")
    .WaitFor(redis).WaitFor(kafka).WaitFor(storage).WaitFor(placement).WaitFor(scheduler);

var orderSvc = builder.AddContainer("api", "aspiredemo-api", "latest")
    .WithHttpEndpoint(targetPort: 8080)
    .WithReference(wiremock)
    // The app services run as prebuilt containers (not AddProject), so they don't get Aspire's
    // automatic OTLP wiring - opt each one in so ServiceDefaults exports to the dashboard and the
    // Dapr sidecar spans have app spans to nest under.
    .WithOtlpExporter()
    .WithDaprSidecarEndpoints("api")
    // inventory-svc has no app-channel of its own (see below) - order-svc talks straight to its
    // sidecar's HTTP API instead (see AspireDemo.Api/InventoryWorkflowClient.cs).
    .WithEnvironment("InventorySidecar__HttpEndpoint", "http://inventory-svc-dapr:3500")
    .WaitFor(wiremock)
    .WaitFor(orderSvcDapr);

// inventory-svc: console app (no HTTP listener) hosting its own reserve/release-stock
// workflows, started remotely by order-svc via this sidecar's HTTP API.
var inventorySvcDapr = AddDaprSidecar("inventory-svc", appPort: null, extraComponentsDir: null)
    .WaitFor(redis).WaitFor(placement).WaitFor(scheduler);

var inventorySvc = builder.AddContainer("inventory-svc", "aspiredemo-inventory-svc", "latest")
    .WithOtlpExporter()
    .WithDaprSidecarEndpoints("inventory-svc")
    .WaitFor(inventorySvcDapr);

// payment-svc: a gRPC service, invoked by order-svc through Dapr gRPC proxying. The sidecar must
// run with --app-protocol grpc so daprd speaks HTTP/2 to the app channel (see AddDaprSidecar).
var paymentSvcDapr = AddDaprSidecar("payment-svc", appPort: 8080, extraComponentsDir: null, appProtocol: "grpc")
    .WaitFor(redis);

var paymentSvc = builder.AddContainer("payment-svc", "aspiredemo-payment-svc", "latest")
    .WithHttpEndpoint(targetPort: 8080)
    .WithOtlpExporter()
    .WithDaprSidecarEndpoints("payment-svc")
    .WaitFor(paymentSvcDapr);

// analytics-svc: a Kafka *input* binding consumer that aggregates order events. Node.js edition -
// a single dependency-free JS file (src/analytics-node/server.js) bind-mounted into a stock node
// image and run directly, no build/npm install. Shows Aspire orchestrating a polyglot service
// alongside the .NET ones, still wired through the same Dapr sidecar and Kafka input binding.
var analyticsSvcDapr = AddDaprSidecar("analytics-svc", appPort: 8080, extraComponentsDir: "analytics-svc")
    .WaitFor(redis).WaitFor(kafka);

var analyticsSvc = builder.AddContainer("analytics-svc", "node", "22-alpine")
    .WithBindMount("../../src/analytics-node/server.js", "/app/server.js", isReadOnly: true)
    .WithArgs("node", "/app/server.js")
    .WithHttpEndpoint(targetPort: 8080)
    // Harmless here (the dependency-free JS ignores the injected OTEL_* env) but kept for parity
    // with the other app containers and the sidecar OTLP story.
    .WithOtlpExporter()
    .WithDaprSidecarEndpoints("analytics-svc")
    .WaitFor(analyticsSvcDapr);

// notification-svc: console app (no HTTP listener) that subscribes to order-lifecycle pub/sub
// events via a Dapr streaming subscription.
var notificationSvcDapr = AddDaprSidecar("notification-svc", appPort: null, extraComponentsDir: null)
    .WaitFor(redis);

var notificationSvc = builder.AddContainer("notification-svc", "aspiredemo-notification-svc", "latest")
    .WithOtlpExporter()
    .WithDaprSidecarEndpoints("notification-svc")
    .WaitFor(notificationSvcDapr);

// Dapr only exposes metrics as a Prometheus scrape endpoint (:9090 on each sidecar), but the
// Aspire dashboard ingests OTLP (push) only - it doesn't scrape. This collector bridges the gap:
// it scrapes every sidecar and forwards the metrics over OTLP to the dashboard. .WithOtlpExporter()
// injects the container-reachable dashboard endpoint, which otel-collector/config.yaml reads via
// ${env:OTEL_EXPORTER_OTLP_ENDPOINT}. See otel-collector/config.yaml for the scrape targets.
builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "0.157.0")
    .WithBindMount("../../otel-collector/config.yaml", "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    .WithOtlpExporter()
    .WaitFor(orderSvcDapr)
    .WaitFor(inventorySvcDapr)
    .WaitFor(paymentSvcDapr)
    .WaitFor(analyticsSvcDapr)
    .WaitFor(notificationSvcDapr);

builder.Build().Run();

// Builds a daprd sidecar container for `appId`. Every sidecar mounts the shared components
// (state store, pub/sub) read-only; `extraComponentsDir`, when given, additionally mounts
// dapr/components/{extraComponentsDir} - used for bindings that must NOT be loaded by every
// sidecar (see the comment in dapr/components/order-svc/order-events.yaml). Apps with no HTTP
// app-channel (inventory-svc, notification-svc - see their Program.cs) pass appPort: null.
IResourceBuilder<ContainerResource> AddDaprSidecar(string appId, int? appPort, string? extraComponentsDir, string appProtocol = "http")
{
    List<string> args =
    [
        "./daprd",
        "--app-id", appId,
        "--dapr-http-port", "3500",
        "--dapr-grpc-port", "50001",
        "--placement-host-address", "placement:50006",
        "--scheduler-host-address", "scheduler:50007",
        "--config", "/dapr-config/dapr-config.yaml",
        "--resources-path", "/components/shared"
    ];

    if (appPort is { } port)
    {
        args.AddRange(["--app-port", port.ToString(), "--app-channel-address", appId]);
        // Defaults to http; pass "grpc" for services whose app channel is gRPC (payment-svc), so
        // daprd talks HTTP/2 to the app and gRPC service-invocation proxying works.
        if (appProtocol != "http")
        {
            args.AddRange(["--app-protocol", appProtocol]);
        }
    }

    // Bind mount sources are relative to this AppHost project's directory (src/AspireDemo.AppHost),
    // not the repo root, hence "../../dapr/...".
    var sidecar = builder.AddContainer($"{appId}-dapr", "daprio/daprd", DaprRuntimeTag)
        .WithBindMount("../../dapr/config", "/dapr-config", isReadOnly: true)
        .WithBindMount("../../dapr/components/shared", "/components/shared", isReadOnly: true);

    if (extraComponentsDir is not null)
    {
        args.AddRange(["--resources-path", $"/components/{extraComponentsDir}"]);
        sidecar = sidecar.WithBindMount($"../../dapr/components/{extraComponentsDir}", $"/components/{extraComponentsDir}", isReadOnly: true);
    }

    return sidecar.WithArgs(args.ToArray())
        // WithOtlpExporter isn't automatic for container resources (only AddProject gets it), so we
        // opt these raw daprd containers in explicitly. It injects a container-network-reachable
        // OTEL_EXPORTER_OTLP_ENDPOINT (+ OTEL_EXPORTER_OTLP_PROTOCOL=grpc), which daprd reads at
        // startup via SetTracingSpecFromEnv - sidecar spans then land in the Aspire dashboard.
        .WithOtlpExporter()
        // daprd defaults its OTLP exporter to TLS (isSecure=true) and would attempt a TLS handshake
        // against the dashboard's plaintext OTLP endpoint. Run the AppHost under its "http" launch
        // profile (plaintext dashboard OTLP) and mark the hop insecure so the export succeeds.
        .WithEnvironment("OTEL_EXPORTER_OTLP_INSECURE", "true");
}

// Points an app container at its own sidecar's HTTP/gRPC endpoints. The Dapr .NET SDK (DaprClient,
// DaprWorkflowClient, DaprPublishSubscribeClient) reads DAPR_HTTP_ENDPOINT/DAPR_GRPC_ENDPOINT
// automatically - needed here because app and sidecar are separate containers, not localhost.
static class DaprAppExtensions
{
    public static IResourceBuilder<ContainerResource> WithDaprSidecarEndpoints(this IResourceBuilder<ContainerResource> app, string appId) =>
        app.WithEnvironment("DAPR_HTTP_ENDPOINT", $"http://{appId}-dapr:3500")
            .WithEnvironment("DAPR_GRPC_ENDPOINT", $"http://{appId}-dapr:50001");
}

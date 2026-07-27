# Dapr Showcase

A Dapr showcase orchestrated locally by .NET Aspire: an **order-fulfillment saga** that exercises
five Dapr building blocks - state management, pub/sub, workflow, an Azure Storage binding, and a
Kafka binding - using only locally-runnable containers. No cloud services and no Dapr CLI. One
service (analytics-svc) is Node.js, but it runs from a stock `node` container image, so you 
don't need Node.js installed locally. Prerequisites are Docker Desktop (Linux), the .NET 10 SDK,
and the Aspire CLI.

## Scenario

`POST /orders` on **order-svc** kicks off a Dapr Workflow saga: reserve inventory -> charge
payment -> get a shipping quote from a third-party carrier API (stood in for by a
[WireMock](https://wiremock.org/dotnet/) container) -> write an invoice -> stream the order event
-> mark shipped. If payment fails (or stock is insufficient), the saga compensates by refunding
and/or releasing the reserved stock.

| Dapr building block | Backing container | Role in the demo |
|---|---|---|
| **State management** | Redis | Order state (order-svc), stock levels (inventory-svc), and the actor state store backing Dapr Workflow |
| **Pub/sub** | Redis | order-svc publishes order-lifecycle events; notification-svc subscribes via a streaming subscription |
| **Workflow** | Dapr runtime (placement + scheduler) | order-svc hosts the fulfillment saga; inventory-svc hosts its own reserve/release-stock workflows, started remotely by order-svc via inventory-svc's sidecar HTTP API |
| **Azure Storage binding** | Azurite | `bindings.azure.blobstorage` **output** binding writes an invoice document to a blob container |
| **Kafka binding** | Redpanda-compatible Kafka (Confluent local image, via `Aspire.Hosting.Kafka`) | order-svc's **output** binding streams order events to a topic; analytics-svc (a Node.js service) consumes and aggregates them via its **input** binding |

## Services

| Service | Kind | Dapr features exercised |
|---|---|---|
| **`src/AspireDemo.Api`** (app-id `api`, the order-svc / front door) | ASP.NET Core minimal API | `POST /orders`, `GET /orders/{id}`; hosts the fulfillment **workflow**; **state** (order); **pub/sub** publish; **service invocation** -> payment-svc; calls inventory-svc's sidecar workflow API directly; **blob output binding** (invoice); **Kafka output binding** (order events); calls WireMock for the shipping quote |
| **`src/AspireDemo.InventoryService`** (app-id `inventory-svc`) | Plain console app, no HTTP listener | **State** (stock, seeded on startup); hosts its own reserve/release-stock **workflows**, started remotely by order-svc via this app's own Dapr sidecar HTTP API (see below) |
| **`src/AspireDemo.PaymentService`** (app-id `payment-svc`) | ASP.NET Core minimal API | `POST /charge`, `POST /refund`; a Dapr **service-invocation** target; **state** (idempotent charge records) |
| **`src/analytics-node/server.js`** (app-id `analytics-svc`) | Node.js HTTP server (stock `node` image, single dependency-free file, no build/npm install) | **Kafka input binding** consumer (`POST /order-events`); `GET /stats` exposes a running total. A polyglot service orchestrated by Aspire alongside the .NET ones, still wired through its own Dapr sidecar |
| **`src/AspireDemo.NotificationService`** (app-id `notification-svc`) | Plain console app, no HTTP listener | **Pub/sub** subscriber via a Dapr *streaming* subscription (`DaprPublishSubscribeClient.SubscribeAsync`) - logs order-lifecycle events |
| **`src/AspireDemo.ServiceDefaults`** | Shared library | OpenTelemetry, health checks, service discovery, resilience - referenced by every service above |
| **wiremock** (AppHost resource `wiremock`) | Container | Stands in for the third-party carrier/shipping-quote API called by the workflow |

### Why some services have no HTTP listener

Dapr service invocation, pub/sub's declarative `/dapr/subscribe` route, and binding *input*
callbacks all require the sidecar to call back into the app over HTTP - that's the usual reason
every Dapr app has a web server. Two mechanisms don't:

- **Dapr Workflow** talks to its app over a connection the *app* opens to its *own* sidecar, not
  the other way around. inventory-svc uses this to host its own reserve/release-stock workflows
  as a **plain console app**. order-svc's saga starts and awaits those workflows by calling
  inventory-svc's sidecar's HTTP workflow API directly (`http://inventory-svc-dapr:3500/v1.0/workflows/...`)
  - a "multi-app workflow" pattern, not service invocation - and reads the result back from the
    shared Dapr state store, since inventory-svc's workflow writes its outcome there as its last
    activity (see `AspireDemo.Api/InventoryWorkflowClient.cs` and
    `AspireDemo.InventoryService/RecordWorkflowResultActivity.cs`).
- **Streaming pub/sub subscriptions** (`Dapr.Messaging`) are pulled by the app from its sidecar
  over a long-lived stream, so the app never needs to expose an endpoint either. notification-svc
  uses this as a **plain console app**.

payment-svc and analytics-svc *are* reached via HTTP (service invocation and a Kafka input
binding callback, respectively), so they run a web server - payment-svc as a minimal ASP.NET Core
API, analytics-svc as a small Node.js HTTP server.

## Infrastructure containers (Aspire-managed)

- **Redis** (`Aspire.Hosting.Redis`, `AddRedis("redis").WithPassword(null)`) - state + pub/sub. No
  password, so the (necessarily static) Dapr component yaml doesn't need to read an
  Aspire-generated secret.
- **Kafka** (`Aspire.Hosting.Kafka`, `AddKafka("kafka")`)
- **Azurite** (`Aspire.Hosting.Azure.Storage`, `AddAzureStorage("storage").RunAsEmulator()`)
- **placement** (`AddContainer("placement", "daprio/placement", ...)`) - actor placement, required
  because Dapr Workflow uses actors internally
- **scheduler** (`AddContainer("scheduler", "daprio/scheduler", ...)`) - workflow reminders (Dapr
  >= 1.14), backed by a small Aspire-managed volume for its embedded etcd store

Every `daprio/*` image is pinned to the same runtime tag (`DaprRuntimeTag` in `AppHost.cs`),
matching the `Dapr.*` NuGet package versions used by the .NET projects.

## Dapr components (yaml - the one unavoidable hand-authored config)

Bindings/components can't be scaffolded by a CLI and the binding metadata *must* be yaml. Rather
than one shared directory mounted into every sidecar, components live under `dapr/components/`
split by scope, because an *input* binding activates for every sidecar that loads it (it starts
consuming immediately, regardless of whether the app has a matching route) - if every sidecar
loaded one bidirectional Kafka component, every app (not just analytics-svc) would compete for
partitions in the same consumer group:

- `dapr/components/shared/` - mounted into **every** sidecar: `statestore.yaml` (`state.redis`,
  `actorStateStore: "true"` for Dapr Workflow, `keyPrefix: none` so order-svc and inventory-svc can
  read/write a shared set of keys) and `pubsub.yaml` (`pubsub.redis`).
- `dapr/components/order-svc/` - mounted only into order-svc's sidecar: `invoice-blob.yaml`
  (`bindings.azure.blobstorage`, output, pointed at Azurite's well-known local dev account) and
  `order-events.yaml` (`bindings.kafka`, **output only**).
- `dapr/components/analytics-svc/` - mounted only into analytics-svc's sidecar:
  `order-events.yaml` (`bindings.kafka`, **input only**, `consumerGroup: analytics`).
- `dapr/config/dapr-config.yaml` - a Dapr `Configuration` enabling tracing
  (`tracing.samplingRate: "1"`). No otel endpoint is set there - Aspire auto-injects
  `OTEL_EXPORTER_OTLP_ENDPOINT` into every container resource it manages (including these raw
  `daprd` containers), and Dapr's OTel exporter reads that env var directly, so sidecar spans land
  in the same Aspire dashboard trace view as the apps'.

`AppHost.cs`'s `AddDaprSidecar` local function wires the `--resources-path`/bind-mount pairs per
sidecar (always `shared`, plus `order-svc` or `analytics-svc` where applicable).

## Product catalog & stock seeding

No manual data step. inventory-svc seeds its own Dapr state store on startup, idempotently: it
checks for a `catalog-seeded` marker key and, if absent, writes a small fixed catalog (4 SKUs:
`widget-1`, `widget-2`, `gadget-1`, `gadget-2`) via `DaprClient.SaveStateAsync`. The seed set lives
in `AspireDemo.InventoryService/CatalogSeeder.cs`, so the demo is deterministic and repeatable
(clear Redis to re-seed).

## Prerequisites

| Tool | Why | Install |
|---|---|---|
| **.NET 10 SDK** | Builds/runs everything; the .NET SDK also builds each .NET service's container image (no Dockerfile). analytics-svc is Node.js but runs from the stock `node` image, so no local Node install is needed | https://dotnet.microsoft.com/download |
| **Docker Desktop**, with its **Linux** engine running | Runs every container in this AppHost (14 in total: 5 apps, 5 `daprd` sidecars, redis, kafka, azurite, placement, scheduler) | https://www.docker.com/products/docker-desktop — after install, make sure it's running and switched to Linux containers |
| **Aspire CLI** | `aspire run` / `aspire publish` etc. | `dotnet tool install --global Aspire.Cli` (or `irm https://aspire.dev/install.ps1 \| iex`) |
| **Aspire project templates** | `aspire-apphost`, `aspire-servicedefaults`, `aspire-xunit` templates used to scaffold this repo | `dotnet new install Aspire.ProjectTemplates` |

Verify with `dotnet --version`, `aspire --version`, `docker info`.

Container parity is the only supported run mode now (no `RUN_*_AS_PROJECT` toggle): every app
talks to its own `daprd` sidecar container by container-network name
(`<app>-dapr:3500`/`:50001`), which only works when the app itself is also a container on that
same network.

### Recommended Claude Code tooling for this repo

**[dotnet/skills](https://github.com/dotnet/skills)** — the official .NET team's plugin
marketplace (general C#/.NET help: ASP.NET Core, testing, template scaffolding, MSBuild). Install
once per machine from an interactive Claude Code session:
```
/plugin marketplace add dotnet/skills
/plugin install dotnet-aspnetcore@dotnet-agent-skills
/plugin install dotnet-test@dotnet-agent-skills
```
(`/plugin` is interactive-only, so this can't be scripted — run it yourself once.)

## How this was scaffolded

```powershell
dotnet new sln -n AspireDemo
dotnet new gitignore

dotnet new aspire-apphost -o src/AspireDemo.AppHost -n AspireDemo.AppHost
dotnet new aspire-servicedefaults -o src/AspireDemo.ServiceDefaults -n AspireDemo.ServiceDefaults
dotnet new webapi -o src/AspireDemo.Api -n AspireDemo.Api
dotnet new webapi -o src/AspireDemo.PaymentService -n AspireDemo.PaymentService
dotnet new console -o src/AspireDemo.InventoryService -n AspireDemo.InventoryService
dotnet new console -o src/AspireDemo.NotificationService -n AspireDemo.NotificationService
dotnet new aspire-xunit -o tests/AspireDemo.Tests -n AspireDemo.Tests

dotnet sln add (every .csproj above)

dotnet add src/AspireDemo.Api reference src/AspireDemo.ServiceDefaults
# ...and the same for every other service project

dotnet add src/AspireDemo.Api package Dapr.AspNetCore
dotnet add src/AspireDemo.Api package Dapr.Workflow
dotnet add src/AspireDemo.InventoryService package Dapr.Workflow
dotnet add src/AspireDemo.InventoryService package Dapr.Client
dotnet add src/AspireDemo.PaymentService package Dapr.AspNetCore
dotnet add src/AspireDemo.NotificationService package Dapr.Messaging

dotnet add src/AspireDemo.AppHost package Aspire.Hosting.Redis
dotnet add src/AspireDemo.AppHost package Aspire.Hosting.Kafka
dotnet add src/AspireDemo.AppHost package Aspire.Hosting.Azure.Storage
dotnet add src/AspireDemo.AppHost package WireMock.Net.Aspire
dotnet add src/AspireDemo.AppHost reference (every service project)

dotnet add tests/AspireDemo.Tests reference src/AspireDemo.AppHost
```

Everything under `Program.cs`/`AppHost.cs`/the workflow & activity classes/the test files, the two
container-image MSBuild properties per .NET service csproj (`ContainerRepository`,
`ContainerImageTag` — needed so the AppHost's `AddContainer` references are deterministic),
`src/analytics-node/server.js` (the Node.js analytics-svc, bind-mounted into a stock `node`
container - no scaffolding, no build), and `dapr/**/*.yaml` (binding/component metadata can't be
scaffolded by any CLI) were hand-authored afterwards.

## Running it

```powershell
# 1. Build every .NET service's container image (re-run after changing any .NET service's code).
# analytics-svc (Node.js) needs no build - its server.js is bind-mounted into a stock node image.
./build.ps1

# 2. Run the AppHost
aspire run
```

Open the Aspire dashboard link that's printed. You should see all 5 app containers, their 5
`daprd` sidecars, `placement`, `scheduler`, `redis`, `kafka`, and `storage` (Azurite) come up
healthy/running.

WireMock has no stubs configured yet in this mode (there are no static mapping files - see
[Configuring stubs](#configuring-stubs-wiremock-admin-api) below), so an order's shipping-quote
step will fail until you configure one.

### Try it end-to-end

```powershell
# via the dashboard's endpoint for the "api" resource
curl -X POST http://localhost:<api-port>/orders `
  -H "Content-Type: application/json" `
  -d '{"items":[{"sku":"widget-1","quantity":1}],"amount":25.00,"destinationPostalCode":"12345"}'

curl http://localhost:<api-port>/orders/<orderId>
```

- **Workflow / state**: the order's `runtimeStatus` progresses `Running` -> `Completed`; inventory
  stock for `widget-1` decrements in Redis.
- **Service invocation**: payment-svc's logs show the charge call.
- **Third-party call**: the shipping-quote activity hits WireMock (404s until you stub it - see
  below).
- **Blob binding**: once shipped, an invoice blob lands in Azurite's `invoices` container.
- **Kafka binding**: analytics-svc's `GET /stats` total increments.
- **Pub/sub**: notification-svc's logs show the order-lifecycle events.
- **Failure path**: an order with `"amount": 100000` or more gets declined by payment-svc, so the
  saga compensates - it releases the reserved stock back, and the order ends up `Failed`.

### Ship parity

`aspire publish` generates deployment artifacts (e.g. a Docker Compose publisher target) using the
same SDK container build. See `aspire publish --help` and the
[Aspire deployment docs](https://aspire.dev) for the publisher that fits your target.

## Configuring stubs (WireMock admin API)

This repo deliberately has **no static WireMock mapping files**. Stubs are registered at runtime
against WireMock's admin API using `WireMock.Net.RestClient`'s `IWireMockAdminApi`. The
integration test (`tests/AspireDemo.Tests/OrderFulfillmentTests.cs`) is the reference example: it
boots the AppHost, gets an admin client via `app.CreateWireMockAdminClient("wiremock", "http")`,
posts a mapping for `GET /shipping-quote`, and then asserts the order it posts reflects that quote
once shipped.

To poke at it manually while `aspire run` is up, open the Aspire dashboard, find WireMock's
endpoint URL, and `POST` a mapping to `{wiremockUrl}/__admin/mappings` (see the
[WireMock admin API reference](https://wiremock.org/dotnet/admin-api-reference)).

## Testing

```powershell
dotnet test
```

The integration test boots the real AppHost - all 14 containers - so **run `./build.ps1` first**
so every `aspiredemo-*:latest` image exists locally (analytics-svc runs the pulled stock `node`
image instead, no build needed). Docker Desktop's Linux engine must be running. Expect this to take
a few minutes on a cold run (image pulls for `daprio/*`, `kafka`, `azurite`, `node`, plus catalog
seeding retries while sidecars come up).

## Open items / known trade-offs

- **`daprio/*` runtime tag**: pinned to `1.18.2` in `AppHost.cs` (`DaprRuntimeTag`) — bump it (and
  the matching `Dapr.*` NuGet package versions) as new Dapr releases land.
- **Kafka/Azurite internal ports**: the Dapr component yaml hardcodes `kafka:9093` (Aspire's Kafka
  integration's `PLAINTEXT_INTERNAL` listener - *not* the `9092` primary listener, which advertises
  a host-only `localhost:<random-port>` address that's unreachable from other containers) and
  `storage:10000` as the container-network addresses (Aspire's host-side port mappings are random
  and irrelevant here, since only other *containers* need to reach these by name). If a future
  `Aspire.Hosting.Kafka`/`Aspire.Hosting.Azure.Storage` release changes these defaults, update the
  yaml under `dapr/components/`.

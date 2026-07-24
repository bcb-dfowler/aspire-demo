using System.Text.Json.Serialization;
using AspireDemo.Api;
using Dapr.Workflow;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// "wiremock" is the AppHost resource name for the third-party carrier API this service depends
// on for shipping quotes. In this AppHost it's a WireMock container standing in for a real
// carrier, resolved via Aspire service discovery (see AppHost.cs).
builder.Services.AddHttpClient<CarrierQuoteClient>(client =>
{
    client.BaseAddress = new Uri("http://wiremock");
});

// inventory-svc has no HTTP app-channel of its own (see InventoryWorkflowClient) - order-svc
// talks straight to its Dapr sidecar's HTTP API instead, so this points at that sidecar
// container's port rather than at an Aspire service-discovery endpoint.
builder.Services.AddHttpClient<InventoryWorkflowClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["InventorySidecar:HttpEndpoint"] ?? "http://inventory-svc-dapr:3500");
});

builder.Services.AddDaprClient();

builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<OrderFulfillmentWorkflow>();
    options.RegisterActivity<ReserveInventoryActivity>();
    options.RegisterActivity<ReleaseInventoryActivity>();
    options.RegisterActivity<ChargePaymentActivity>();
    options.RegisterActivity<RefundPaymentActivity>();
    options.RegisterActivity<GetShippingQuoteActivity>();
    options.RegisterActivity<GenerateInvoiceActivity>();
    options.RegisterActivity<PublishOrderEventActivity>();
    options.RegisterActivity<SaveOrderStateActivity>();
    options.RegisterActivity<PublishOrderStatusActivity>();
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseHttpsRedirection();

app.MapPost("/orders", async (OrderRequest request, DaprWorkflowClient workflowClient, CancellationToken cancellationToken) =>
{
    var orderId = Guid.NewGuid().ToString("N");
    await workflowClient.ScheduleNewWorkflowAsync(nameof(OrderFulfillmentWorkflow), orderId, request, startTime: null, cancellation: cancellationToken);
    return Results.Accepted($"/orders/{orderId}", new { orderId });
}).WithName("CreateOrder");

app.MapGet("/orders/{orderId}", async (string orderId, DaprWorkflowClient workflowClient, CancellationToken cancellationToken) =>
{
    var state = await workflowClient.GetWorkflowStateAsync(orderId, getInputsAndOutputs: true, cancellation: cancellationToken);
    if (!state.Exists)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        state.RuntimeStatus,
        Result = state.RuntimeStatus == WorkflowRuntimeStatus.Completed ? state.ReadOutputAs<OrderResult>() : null
    });
}).WithName("GetOrder");

app.Run();

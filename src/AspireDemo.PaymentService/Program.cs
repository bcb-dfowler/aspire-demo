using AspireDemo.PaymentService;
using Dapr.Client;

const string StateStore = "statestore";

// Demo failure trigger: orders at or above this amount are declined, so the saga's compensation
// path (release inventory) has something to exercise end-to-end.
const decimal DeclineThreshold = 100_000m;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDaprClient();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapPost("/charge", async (ChargeRequest request, DaprClient dapr, CancellationToken cancellationToken) =>
{
    var key = $"payment:{request.OrderId}";

    var existing = await dapr.GetStateAsync<PaymentResult>(StateStore, key, cancellationToken: cancellationToken);
    if (existing is not null)
    {
        return Results.Ok(existing);
    }

    var result = request.Amount > 0 && request.Amount < DeclineThreshold
        ? new PaymentResult(true, Guid.NewGuid().ToString("N"), Reason: null)
        : new PaymentResult(false, TransactionId: null, Reason: "declined");

    await dapr.SaveStateAsync(StateStore, key, result, cancellationToken: cancellationToken);
    return Results.Ok(result);
}).WithName("ChargePayment");

app.MapPost("/refund", async (RefundRequest request, DaprClient dapr, CancellationToken cancellationToken) =>
{
    await dapr.DeleteStateAsync(StateStore, $"payment:{request.OrderId}", cancellationToken: cancellationToken);
    return Results.Ok();
}).WithName("RefundPayment");

app.Run();

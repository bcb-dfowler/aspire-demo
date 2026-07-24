using AspireDemo.AnalyticsService;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

var totals = new AnalyticsTotals();

// Dapr's bindings.kafka *input* binding (component name "order-events") POSTs each Kafka message
// here, at a route matching the component's name - see dapr/components/order-events.yaml.
app.MapPost("/order-events", (OrderEvent orderEvent) =>
{
    totals.Record(orderEvent.Amount);
    return Results.Ok();
}).WithName("ConsumeOrderEvent");

app.MapGet("/stats", () => Results.Ok(new StatsResponse(totals.OrderCount, totals.TotalRevenue)))
    .WithName("GetStats");

app.Run();

// In-memory aggregate: fine for a demo, simplest way to show the Kafka input binding lighting up.
internal sealed class AnalyticsTotals
{
    private readonly Lock gate = new();
    private long orderCount;
    private decimal totalRevenue;

    public long OrderCount => orderCount;
    public decimal TotalRevenue => totalRevenue;

    public void Record(decimal amount)
    {
        lock (gate)
        {
            orderCount++;
            totalRevenue += amount;
        }
    }
}

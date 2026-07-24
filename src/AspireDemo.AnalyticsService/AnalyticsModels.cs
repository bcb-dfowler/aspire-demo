namespace AspireDemo.AnalyticsService;

// Mirrors order-svc's OrderEvent contract (see AspireDemo.Api/PublishOrderEventActivity.cs) -
// this is what Dapr's bindings.kafka *input* binding POSTs here for each message on the topic.
public sealed record OrderEvent(string OrderId, decimal Amount);

public sealed record StatsResponse(long OrderCount, decimal TotalRevenue);

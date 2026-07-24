using Dapr.Client;
using Dapr.Workflow;

namespace AspireDemo.Api;

public sealed record OrderStatusChanged(string OrderId, OrderStatus Status);

/// <summary>
/// Publishes an order-lifecycle event via Dapr pub/sub (Redis). notification-svc subscribes to
/// this topic via a streaming subscription.
/// </summary>
public sealed class PublishOrderStatusActivity(DaprClient daprClient) : WorkflowActivity<OrderStatusChanged, object?>
{
    public override async Task<object?> RunAsync(WorkflowActivityContext context, OrderStatusChanged input)
    {
        await daprClient.PublishEventAsync("pubsub", "order-lifecycle", input);
        return null;
    }
}

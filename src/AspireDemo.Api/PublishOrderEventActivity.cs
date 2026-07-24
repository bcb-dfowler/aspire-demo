using Dapr.Client;
using Dapr.Workflow;

namespace AspireDemo.Api;

public sealed record OrderEvent(string OrderId, decimal Amount);

/// <summary>
/// Streams the order event to Kafka via Dapr's bindings.kafka *output* binding. analytics-svc
/// consumes the same topic via a Kafka *input* binding.
/// </summary>
public sealed class PublishOrderEventActivity(DaprClient daprClient) : WorkflowActivity<OrderEvent, object?>
{
    public override async Task<object?> RunAsync(WorkflowActivityContext context, OrderEvent input)
    {
        await daprClient.InvokeBindingAsync("order-events", "create", input);
        return null;
    }
}

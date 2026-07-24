using Dapr.Client;
using Dapr.Workflow;

namespace AspireDemo.Api;

/// <summary>
/// Persists the order's current state to the Dapr state store (Redis).
/// </summary>
public sealed class SaveOrderStateActivity(DaprClient daprClient) : WorkflowActivity<OrderState, object?>
{
    public override async Task<object?> RunAsync(WorkflowActivityContext context, OrderState input)
    {
        await daprClient.SaveStateAsync("statestore", $"order:{input.OrderId}", input);
        return null;
    }
}

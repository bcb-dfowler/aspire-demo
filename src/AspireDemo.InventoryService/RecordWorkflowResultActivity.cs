using Dapr.Client;
using Dapr.Workflow;

namespace AspireDemo.InventoryService;

public sealed record WorkflowResultRecord(string InstanceId, ReserveStockResult Result);

/// <summary>
/// Writes the workflow's outcome to the (shared, unprefixed - see dapr/components/statestore.yaml)
/// Dapr state store, since order-svc calls this workflow via its sidecar's HTTP API rather than
/// through the .NET SDK, so it has no other way to read the result back.
/// </summary>
public sealed class RecordWorkflowResultActivity(DaprClient daprClient) : WorkflowActivity<WorkflowResultRecord, object?>
{
    public override async Task<object?> RunAsync(WorkflowActivityContext context, WorkflowResultRecord input)
    {
        await daprClient.SaveStateAsync("statestore", $"workflow-result:{input.InstanceId}", input.Result);
        return null;
    }
}

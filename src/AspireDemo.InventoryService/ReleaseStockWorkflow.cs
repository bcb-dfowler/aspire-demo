using Dapr.Workflow;

namespace AspireDemo.InventoryService;

/// <summary>
/// Started remotely by order-svc via this app's own Dapr sidecar HTTP API (instance id
/// "ReleaseStockWorkflow-{orderId}") as saga compensation - see AspireDemo.Api/InventoryWorkflowClient.cs.
/// </summary>
public sealed class ReleaseStockWorkflow : Workflow<ReserveStockInput, ReserveStockResult>
{
    public override async Task<ReserveStockResult> RunAsync(WorkflowContext context, ReserveStockInput input)
    {
        var result = await context.CallActivityAsync<ReserveStockResult>(nameof(ReleaseStockActivity), input);
        await context.CallActivityAsync(nameof(RecordWorkflowResultActivity), new WorkflowResultRecord(context.InstanceId, result));
        return result;
    }
}

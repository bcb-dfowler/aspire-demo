using Dapr.Workflow;

namespace AspireDemo.Api;

/// <summary>
/// Compensating activity: releases previously reserved stock by starting inventory-svc's
/// ReleaseStockWorkflow. Best-effort - logs rather than throws, since compensation itself
/// should not fail the saga.
/// </summary>
public sealed class ReleaseInventoryActivity(InventoryWorkflowClient inventory, ILogger<ReleaseInventoryActivity> logger) : WorkflowActivity<ReserveInventoryRequest, object?>
{
    public override async Task<object?> RunAsync(WorkflowActivityContext context, ReserveInventoryRequest input)
    {
        var result = await inventory.RunWorkflowAsync("ReleaseStockWorkflow", input.OrderId, input);
        if (!result.Success)
        {
            logger.LogWarning("Releasing inventory for order {OrderId} reported failure: {Reason}", input.OrderId, result.Reason);
        }

        return null;
    }
}

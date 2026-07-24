using Dapr.Workflow;

namespace AspireDemo.Api;

public sealed record StockLineItem(string Sku, int Quantity);

public sealed record ReserveInventoryRequest(string OrderId, IReadOnlyList<StockLineItem> Items);

/// <summary>
/// Reserves stock by starting inventory-svc's ReserveStockWorkflow on its own sidecar and
/// awaiting the outcome. Throws if inventory-svc reports insufficient stock, so the orchestrator's
/// catch block can run compensation.
/// </summary>
public sealed class ReserveInventoryActivity(InventoryWorkflowClient inventory) : WorkflowActivity<ReserveInventoryRequest, object?>
{
    public override async Task<object?> RunAsync(WorkflowActivityContext context, ReserveInventoryRequest input)
    {
        var result = await inventory.RunWorkflowAsync("ReserveStockWorkflow", input.OrderId, input);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Inventory reservation failed for order {input.OrderId}: {result.Reason}");
        }

        return null;
    }
}

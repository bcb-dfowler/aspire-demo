using Dapr.Client;
using Dapr.Workflow;

namespace AspireDemo.InventoryService;

/// <summary>
/// Compensating activity: releases stock previously reserved for an order, e.g. because a later
/// saga step (payment) failed. Best-effort with optimistic-concurrency retries.
/// </summary>
public sealed class ReleaseStockActivity(DaprClient daprClient) : WorkflowActivity<ReserveStockInput, ReserveStockResult>
{
    private const string StateStore = "statestore";
    private const int MaxConcurrencyRetries = 5;

    public override async Task<ReserveStockResult> RunAsync(WorkflowActivityContext context, ReserveStockInput input)
    {
        foreach (var item in input.Items)
        {
            var key = $"sku:{item.Sku}";
            for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
            {
                var (current, etag) = await daprClient.GetStateAndETagAsync<CatalogItem?>(StateStore, key);
                if (current is null)
                {
                    break;
                }

                var restored = current with { Quantity = current.Quantity + item.Quantity };
                if (await daprClient.TrySaveStateAsync(StateStore, key, restored, etag))
                {
                    break;
                }
            }
        }

        return new ReserveStockResult(true, Reason: null);
    }
}

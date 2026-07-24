using Dapr.Client;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;

namespace AspireDemo.InventoryService;

/// <summary>
/// Decrements stock for each requested SKU using optimistic concurrency (ETags), so concurrent
/// orders can't oversell the same SKU. If any item can't be reserved (missing SKU or insufficient
/// stock), rolls back the items already decremented in this same request and fails.
/// </summary>
public sealed class ReserveStockActivity(DaprClient daprClient, ILogger<ReserveStockActivity> logger) : WorkflowActivity<ReserveStockInput, ReserveStockResult>
{
    private const string StateStore = "statestore";
    private const int MaxConcurrencyRetries = 5;

    public override async Task<ReserveStockResult> RunAsync(WorkflowActivityContext context, ReserveStockInput input)
    {
        var reserved = new List<StockItem>();

        foreach (var requested in input.Items)
        {
            var (ok, reason) = await TryDecrementAsync(requested);
            if (!ok)
            {
                await RollBackAsync(reserved);
                return new ReserveStockResult(false, reason);
            }

            reserved.Add(requested);
        }

        return new ReserveStockResult(true, Reason: null);
    }

    private async Task<(bool Ok, string? Reason)> TryDecrementAsync(StockItem requested)
    {
        var key = $"sku:{requested.Sku}";

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var (item, etag) = await daprClient.GetStateAndETagAsync<CatalogItem?>(StateStore, key);
            if (item is null)
            {
                return (false, $"Unknown SKU '{requested.Sku}'");
            }

            if (item.Quantity < requested.Quantity)
            {
                return (false, $"Insufficient stock for '{requested.Sku}': requested {requested.Quantity}, available {item.Quantity}");
            }

            var updated = item with { Quantity = item.Quantity - requested.Quantity };
            if (await daprClient.TrySaveStateAsync(StateStore, key, updated, etag))
            {
                return (true, null);
            }

            logger.LogInformation("Concurrent update detected for {Sku}, retrying (attempt {Attempt})", requested.Sku, attempt + 1);
        }

        return (false, $"Could not reserve '{requested.Sku}' due to concurrent updates");
    }

    private async Task RollBackAsync(IReadOnlyList<StockItem> reserved)
    {
        foreach (var item in reserved)
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
    }
}

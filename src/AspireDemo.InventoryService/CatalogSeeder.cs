using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace AspireDemo.InventoryService;

/// <summary>
/// Seeds a small, fixed product catalog into the Dapr state store on startup, idempotently (a
/// marker key guards against re-seeding). Clear Redis to re-seed. Retries briefly since this runs
/// before we know the Dapr sidecar's API is accepting requests yet.
/// </summary>
public static class CatalogSeeder
{
    private const string StateStore = "statestore";
    private const string SeededMarkerKey = "catalog-seeded";

    private static readonly CatalogItem[] Catalog =
    [
        new("widget-1", "Widget", 9.99m, 50),
        new("widget-2", "Deluxe Widget", 19.99m, 25),
        new("gadget-1", "Gadget", 14.99m, 40),
        new("gadget-2", "Deluxe Gadget", 29.99m, 15)
    ];

    public static async Task SeedAsync(DaprClient daprClient, ILogger logger, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var alreadySeeded = await daprClient.GetStateAsync<bool?>(StateStore, SeededMarkerKey, cancellationToken: cancellationToken);
                if (alreadySeeded == true)
                {
                    logger.LogInformation("Catalog already seeded, skipping");
                    return;
                }

                foreach (var item in Catalog)
                {
                    await daprClient.SaveStateAsync(StateStore, $"sku:{item.Sku}", item, cancellationToken: cancellationToken);
                }

                await daprClient.SaveStateAsync(StateStore, SeededMarkerKey, true, cancellationToken: cancellationToken);
                logger.LogInformation("Seeded catalog with {Count} SKUs", Catalog.Length);
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "Catalog seed attempt {Attempt} failed, retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }
}

namespace AspireDemo.InventoryService;

// Mirrors order-svc's StockLineItem/ReserveInventoryRequest contract (see
// AspireDemo.Api/ReserveInventoryActivity.cs) for deserializing the workflow input this service
// receives, without referencing the Api project directly - these are independently deployable
// services that only agree on the wire shape.
public sealed record StockItem(string Sku, int Quantity);

public sealed record ReserveStockInput(string OrderId, IReadOnlyList<StockItem> Items);

public sealed record ReserveStockResult(bool Success, string? Reason);

public sealed record CatalogItem(string Sku, string Name, decimal Price, int Quantity);

namespace AspireDemo.Api;

public sealed record OrderItem(string Sku, int Quantity);

public sealed record OrderRequest(IReadOnlyList<OrderItem> Items, decimal Amount, string DestinationPostalCode);

public enum OrderStatus
{
    Created,
    InventoryReserved,
    PaymentCharged,
    Shipped,
    Failed
}

/// <summary>
/// The order-svc view of an order, persisted to the Dapr state store as the saga progresses.
/// </summary>
public sealed record OrderState(string OrderId, OrderStatus Status, OrderRequest Request, ShippingQuote? Quote = null, string? InvoiceBlobName = null, string? FailureReason = null);

public sealed record OrderResult(string OrderId, OrderStatus Status, ShippingQuote? Quote, string? InvoiceBlobName, string? FailureReason);

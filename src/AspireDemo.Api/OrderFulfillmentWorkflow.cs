using Dapr.Workflow;

namespace AspireDemo.Api;

/// <summary>
/// The order fulfillment saga: reserve inventory -> charge payment -> get a shipping quote from
/// the carrier (WireMock) -> generate an invoice -> publish the order event -> mark shipped.
/// On failure after inventory/payment succeeded, compensates by releasing stock and/or refunding.
/// </summary>
public sealed class OrderFulfillmentWorkflow : Workflow<OrderRequest, OrderResult>
{
    public override async Task<OrderResult> RunAsync(WorkflowContext context, OrderRequest input)
    {
        var orderId = context.InstanceId;
        var items = input.Items.Select(i => new StockLineItem(i.Sku, i.Quantity)).ToList();

        await context.CallActivityAsync(nameof(SaveOrderStateActivity), new OrderState(orderId, OrderStatus.Created, input));
        await context.CallActivityAsync(nameof(PublishOrderStatusActivity), new OrderStatusChanged(orderId, OrderStatus.Created));

        var inventoryReserved = false;
        var paymentCharged = false;
        PaymentResult? payment = null;

        try
        {
            await context.CallActivityAsync(nameof(ReserveInventoryActivity), new ReserveInventoryRequest(orderId, items));
            inventoryReserved = true;
            await context.CallActivityAsync(nameof(SaveOrderStateActivity), new OrderState(orderId, OrderStatus.InventoryReserved, input));
            await context.CallActivityAsync(nameof(PublishOrderStatusActivity), new OrderStatusChanged(orderId, OrderStatus.InventoryReserved));

            payment = await context.CallActivityAsync<PaymentResult>(nameof(ChargePaymentActivity), new ChargePaymentRequest(orderId, input.Amount));
            paymentCharged = true;
            await context.CallActivityAsync(nameof(SaveOrderStateActivity), new OrderState(orderId, OrderStatus.PaymentCharged, input));
            await context.CallActivityAsync(nameof(PublishOrderStatusActivity), new OrderStatusChanged(orderId, OrderStatus.PaymentCharged));

            var quote = await context.CallActivityAsync<ShippingQuote>(nameof(GetShippingQuoteActivity), new ShippingQuoteRequest(input.DestinationPostalCode));

            var invoice = await context.CallActivityAsync<InvoiceResult>(
                nameof(GenerateInvoiceActivity), new GenerateInvoiceRequest(orderId, input, quote, payment));

            await context.CallActivityAsync(nameof(PublishOrderEventActivity), new OrderEvent(orderId, input.Amount));

            var shippedState = new OrderState(orderId, OrderStatus.Shipped, input, quote, invoice.BlobName);
            await context.CallActivityAsync(nameof(SaveOrderStateActivity), shippedState);
            await context.CallActivityAsync(nameof(PublishOrderStatusActivity), new OrderStatusChanged(orderId, OrderStatus.Shipped));

            return new OrderResult(orderId, OrderStatus.Shipped, quote, invoice.BlobName, FailureReason: null);
        }
        catch (WorkflowTaskFailedException ex)
        {
            if (paymentCharged)
            {
                await context.CallActivityAsync(nameof(RefundPaymentActivity), new RefundPaymentRequest(orderId));
            }

            if (inventoryReserved)
            {
                await context.CallActivityAsync(nameof(ReleaseInventoryActivity), new ReserveInventoryRequest(orderId, items));
            }

            var failedState = new OrderState(orderId, OrderStatus.Failed, input, FailureReason: ex.Message);
            await context.CallActivityAsync(nameof(SaveOrderStateActivity), failedState);
            await context.CallActivityAsync(nameof(PublishOrderStatusActivity), new OrderStatusChanged(orderId, OrderStatus.Failed));

            return new OrderResult(orderId, OrderStatus.Failed, Quote: null, InvoiceBlobName: null, ex.Message);
        }
    }
}

using Dapr.Client;
using Dapr.Workflow;

namespace AspireDemo.Api;

public sealed record GenerateInvoiceRequest(string OrderId, OrderRequest Order, ShippingQuote Quote, PaymentResult Payment);

public sealed record InvoiceResult(string BlobName);

/// <summary>
/// Writes an invoice document to Azure Blob Storage (Azurite locally) via Dapr's
/// bindings.azure.blobstorage *output* binding.
/// </summary>
public sealed class GenerateInvoiceActivity(DaprClient daprClient) : WorkflowActivity<GenerateInvoiceRequest, InvoiceResult>
{
    public override async Task<InvoiceResult> RunAsync(WorkflowActivityContext context, GenerateInvoiceRequest input)
    {
        var blobName = $"invoice-{input.OrderId}.json";

        var invoice = new
        {
            input.OrderId,
            input.Order.Items,
            input.Order.Amount,
            Carrier = input.Quote.Carrier,
            ShippingCost = input.Quote.Cost,
            EstimatedDays = input.Quote.EstimatedDays,
            TransactionId = input.Payment.TransactionId
        };

        await daprClient.InvokeBindingAsync(
            "invoice-blob",
            "create",
            invoice,
            new Dictionary<string, string> { ["blobName"] = blobName });

        return new InvoiceResult(blobName);
    }
}

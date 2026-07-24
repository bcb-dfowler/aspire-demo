using Dapr.Workflow;

namespace AspireDemo.Api;

public sealed record ShippingQuoteRequest(string DestinationPostalCode);

/// <summary>
/// Calls the third-party carrier API (WireMock) for a shipping-rate quote.
/// </summary>
public sealed class GetShippingQuoteActivity(CarrierQuoteClient carrier) : WorkflowActivity<ShippingQuoteRequest, ShippingQuote>
{
    public override Task<ShippingQuote> RunAsync(WorkflowActivityContext context, ShippingQuoteRequest input) =>
        carrier.GetShippingQuoteAsync(input.DestinationPostalCode);
}

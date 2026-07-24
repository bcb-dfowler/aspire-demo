namespace AspireDemo.Api;

/// <summary>
/// Calls the third-party carrier API for a shipping-rate quote, as part of the order
/// fulfillment workflow.
/// </summary>
public sealed class CarrierQuoteClient(HttpClient httpClient)
{
    public async Task<ShippingQuote> GetShippingQuoteAsync(string destinationPostalCode, CancellationToken cancellationToken = default)
    {
        var quote = await httpClient.GetFromJsonAsync<ShippingQuote>(
            $"/shipping-quote?postalCode={Uri.EscapeDataString(destinationPostalCode)}", cancellationToken);
        return quote ?? throw new InvalidOperationException("Carrier API returned no quote.");
    }
}

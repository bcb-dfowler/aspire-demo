namespace AspireDemo.Api;

/// <summary>
/// A shipping-rate quote as returned by the third-party carrier API. In this demo that API is a
/// WireMock container standing in for the real carrier (see the AppHost and the integration
/// tests, which configure the stub via the WireMock admin API).
/// </summary>
public sealed record ShippingQuote(string Carrier, decimal Cost, int EstimatedDays);

namespace AspireDemo.Api;

/// <summary>
/// A single day's forecast as returned by the third-party weather API. In this demo that
/// API is a WireMock container standing in for the real external provider (see the AppHost
/// and the integration tests, which configure the stub via the WireMock admin API).
/// </summary>
public sealed record ExternalForecastDay(DateOnly Date, int TemperatureC, string? Summary);

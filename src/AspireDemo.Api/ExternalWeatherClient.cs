namespace AspireDemo.Api;

/// <summary>
/// Calls the third-party weather forecast API that this service depends on.
/// </summary>
public sealed class ExternalWeatherClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ExternalForecastDay>> GetForecastAsync(CancellationToken cancellationToken = default)
    {
        var forecast = await httpClient.GetFromJsonAsync<List<ExternalForecastDay>>("/forecast", cancellationToken);
        return forecast ?? [];
    }
}

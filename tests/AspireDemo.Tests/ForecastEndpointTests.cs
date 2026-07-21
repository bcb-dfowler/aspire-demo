using System.Net.Http.Json;
using WireMock.Admin.Mappings;

namespace AspireDemo.Tests.Tests;

public class ForecastEndpointTests(AspireAppHostFixture fixture) : IClassFixture<AspireAppHostFixture>
{
    [Fact]
    public async Task GetForecast_ReturnsDataFromExternalApiStub()
    {
        var cancellationToken = CancellationToken.None;

        // Program the third-party API stub at runtime via WireMock's admin API - no static
        // mapping files. This is what makes the "external API" respond the way this test expects.
        await using var mapping = await fixture.WireMockAdmin.PostScopedMappingAsync(builder =>
        {
            builder.WithRequest(request => request.UsingGet().WithPath("/forecast"))
                .WithResponse(response => response.WithStatusCode(200).WithBodyAsJson(new[]
                {
                    new { date = "2026-07-22", temperatureC = 21, summary = "Mild" }
                }));
        }, cancellationToken);

        using var httpClient = fixture.App.CreateHttpClient("api");
        using var response = await httpClient.GetAsync("/forecast", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var forecast = await response.Content.ReadFromJsonAsync<List<ForecastDayDto>>(cancellationToken);
        var day = Assert.Single(forecast!);
        Assert.Equal(new DateOnly(2026, 7, 22), day.Date);
        Assert.Equal(21, day.TemperatureC);
        Assert.Equal("Mild", day.Summary);
    }

    // Mirrors AspireDemo.Api's ExternalForecastDay contract for deserializing the API's response
    // in this test project (which doesn't reference the API project directly).
    private sealed record ForecastDayDto(DateOnly Date, int TemperatureC, string? Summary);
}

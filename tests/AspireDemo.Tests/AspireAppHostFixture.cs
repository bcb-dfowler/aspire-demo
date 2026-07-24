using Aspire.Hosting;
using Microsoft.Extensions.Logging;
using WireMock.Client;

namespace AspireDemo.Tests.Tests;

/// <summary>
/// Boots the AspireDemo AppHost once per test class and tears it down afterwards, so individual
/// tests only need to worry about stubbing WireMock and calling the API.
/// </summary>
public sealed class AspireAppHostFixture : IAsyncLifetime
{
    // Generous: cold-starts 14 containers (5 apps, 5 sidecars, redis, kafka, azurite, placement,
    // scheduler), including image pulls the first time.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public DistributedApplication App { get; private set; } = null!;

    public IWireMockAdminApi WireMockAdmin { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var cancellationToken = CancellationToken.None;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AspireDemo_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            // Override the logging filters from the app's configuration
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        App = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await App.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        await App.ResourceNotifications.WaitForResourceHealthyAsync("wiremock", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        WireMockAdmin = App.CreateWireMockAdminClient("wiremock", "http");
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
    }
}

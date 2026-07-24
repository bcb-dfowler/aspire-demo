using System.Text.Json;
using Dapr.Messaging;
using Dapr.Messaging.PublishSubscribe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// This is a plain console app (no ASP.NET Core host), so it builds a DaprPublishSubscribeClient
// directly rather than using Dapr.AspNetCore's AddDaprPubSubClient() DI helper.
builder.Services.AddSingleton(new DaprPublishSubscribeClientBuilder().Build());

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var messagingClient = host.Services.GetRequiredService<DaprPublishSubscribeClient>();

// Messages are pulled by this app from its Dapr sidecar over a long-lived stream, so - unlike
// service invocation or the declarative /dapr/subscribe route - this app never needs to expose an
// HTTP endpoint of its own to receive pub/sub events.
var subscriptionOptions = new DaprSubscriptionOptions(new MessageHandlingPolicy(TimeSpan.FromSeconds(10), TopicResponseAction.Retry));

IAsyncDisposable? subscription = null;
for (var attempt = 1; subscription is null; attempt++)
{
    try
    {
        subscription = await messagingClient.SubscribeAsync(
            "pubsub",
            "order-lifecycle",
            subscriptionOptions,
            HandleOrderLifecycleEventAsync,
            host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
    }
    catch (Exception ex) when (attempt < 10)
    {
        logger.LogWarning(ex, "Subscribe attempt {Attempt} failed, retrying", attempt);
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

await host.RunAsync();

Task<TopicResponseAction> HandleOrderLifecycleEventAsync(TopicMessage message, CancellationToken cancellationToken)
{
    using var payload = JsonDocument.Parse(message.Data);
    logger.LogInformation("order-lifecycle event: {Payload}", payload.RootElement.GetRawText());
    return Task.FromResult(TopicResponseAction.Success);
}

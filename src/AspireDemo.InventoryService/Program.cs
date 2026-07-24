using AspireDemo.InventoryService;
using Dapr.Client;
using Dapr.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// This is a plain console app (no ASP.NET Core host), so it uses Dapr.Client directly rather
// than Dapr.AspNetCore's AddDaprClient() DI helper.
builder.Services.AddSingleton(new DaprClientBuilder().Build());

builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<ReserveStockWorkflow>();
    options.RegisterWorkflow<ReleaseStockWorkflow>();
    options.RegisterActivity<ReserveStockActivity>();
    options.RegisterActivity<ReleaseStockActivity>();
    options.RegisterActivity<RecordWorkflowResultActivity>();
});

using var host = builder.Build();

await CatalogSeeder.SeedAsync(
    host.Services.GetRequiredService<DaprClient>(),
    host.Services.GetRequiredService<ILogger<Program>>());

await host.RunAsync();

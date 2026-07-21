using AspireDemo.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// "wiremock" is the AppHost resource name for the third-party API this service depends on.
// In this AppHost it's a WireMock container standing in for a real external provider;
// resolved via Aspire service discovery (see AppHost.cs).
builder.Services.AddHttpClient<ExternalWeatherClient>(client =>
{
    client.BaseAddress = new Uri("http://wiremock");
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/forecast", async (ExternalWeatherClient client, CancellationToken cancellationToken) =>
    await client.GetForecastAsync(cancellationToken))
    .WithName("GetForecast");

app.Run();

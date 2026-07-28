using System.Net.Http.Json;
using WireMock.Admin.Mappings;

namespace AspireDemo.Tests.Tests;

public class OrderFulfillmentTests(AspireAppHostFixture fixture) : IClassFixture<AspireAppHostFixture>
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task PostOrder_RunsFulfillmentSagaEndToEnd()
    {
        var cancellationToken = CancellationToken.None;

        // Program the third-party carrier API stub at runtime via WireMock's admin API - no
        // static mapping files. This is what makes order-svc's shipping-quote activity, and so
        // the whole saga, resolve deterministically.
        await using var mapping = await fixture.WireMockAdmin.PostScopedMappingAsync(builder =>
        {
            builder.WithRequest(request => request.UsingGet().WithPath("/shipping-quote"))
                .WithResponse(response => response.WithStatusCode(200).WithBodyAsJson(new
                {
                    carrier = "Acme Freight",
                    cost = 12.50,
                    estimatedDays = 3
                }));
        }, cancellationToken);

        using var httpClient = fixture.App.CreateHttpClient("api");

        var orderRequest = new OrderRequestDto(
            Items: [new OrderItemDto("widget-1", 1)],
            Amount: 25.00m,
            DestinationPostalCode: "12345");

        using var createResponse = await httpClient.PostAsJsonAsync("/orders", orderRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<CreateOrderResponseDto>(cancellationToken);
        Assert.NotNull(created);

        var status = await PollUntilTerminalAsync(httpClient, created.OrderId, cancellationToken);

        Assert.Equal("Shipped", status.Result?.Status);
        Assert.NotNull(status.Result?.Quote);
        Assert.Equal("Acme Freight", status.Result!.Quote!.Carrier);
        Assert.Equal(12.50m, status.Result!.Quote!.Cost);
        Assert.False(string.IsNullOrEmpty(status.Result!.InvoiceBlobName));
    }

    [Fact]
    public async Task PostOrder_ExceedingPaymentThreshold_FailsAndReleasesInventory()
    {
        var cancellationToken = CancellationToken.None;

        // The follow-up order (Order B below) reaches the shipping step, so the carrier stub must be
        // in place. Order A fails at payment before shipping, so it never touches this.
        await using var mapping = await fixture.WireMockAdmin.PostScopedMappingAsync(builder =>
        {
            builder.WithRequest(request => request.UsingGet().WithPath("/shipping-quote"))
                .WithResponse(response => response.WithStatusCode(200).WithBodyAsJson(new
                {
                    carrier = "Acme Freight",
                    cost = 12.50,
                    estimatedDays = 3
                }));
        }, cancellationToken);

        using var httpClient = fixture.App.CreateHttpClient("api");

        // Dedicated SKU, seeded with exactly 15 units (see InventoryService/CatalogSeeder.cs) and
        // unused by the happy-path test, so this scenario is self-contained.
        const string sku = "gadget-2";
        const int stock = 15;

        // Order A: reserves the full stock, then trips the payment decline threshold (>= 100,000).
        // Payment is declined *before* the charge succeeds, so the saga compensates by releasing the
        // reserved inventory only - RefundPaymentActivity is intentionally NOT called (there is no
        // successful payment to refund).
        var failingOrder = new OrderRequestDto(
            Items: [new OrderItemDto(sku, stock)],
            Amount: 150_000m,
            DestinationPostalCode: "12345");

        using var failingResponse = await httpClient.PostAsJsonAsync("/orders", failingOrder, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, failingResponse.StatusCode);

        var failingCreated = await failingResponse.Content.ReadFromJsonAsync<CreateOrderResponseDto>(cancellationToken);
        Assert.NotNull(failingCreated);

        var failingStatus = await PollUntilTerminalAsync(httpClient, failingCreated.OrderId, cancellationToken);

        Assert.Equal("Failed", failingStatus.Result?.Status);
        Assert.Contains("declined", failingStatus.Result?.FailureReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Order B: reserves the same full stock at a normal amount. It can only succeed if Order A's
        // reservation was released by the compensation path - so a shipped Order B is direct proof
        // that ReleaseInventoryActivity ran and restored the stock.
        var recoveryOrder = new OrderRequestDto(
            Items: [new OrderItemDto(sku, stock)],
            Amount: 250.00m,
            DestinationPostalCode: "12345");

        using var recoveryResponse = await httpClient.PostAsJsonAsync("/orders", recoveryOrder, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, recoveryResponse.StatusCode);

        var recoveryCreated = await recoveryResponse.Content.ReadFromJsonAsync<CreateOrderResponseDto>(cancellationToken);
        Assert.NotNull(recoveryCreated);

        var recoveryStatus = await PollUntilTerminalAsync(httpClient, recoveryCreated.OrderId, cancellationToken);

        Assert.Equal("Shipped", recoveryStatus.Result?.Status);
    }

    private static async Task<OrderStatusResponseDto> PollUntilTerminalAsync(HttpClient httpClient, string orderId, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PollTimeout);

        while (true)
        {
            using var response = await httpClient.GetAsync($"/orders/{orderId}", timeoutCts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var status = await response.Content.ReadFromJsonAsync<OrderStatusResponseDto>(timeoutCts.Token);
            Assert.NotNull(status);

            if (status.RuntimeStatus is "Completed" or "Failed" or "Terminated")
            {
                return status;
            }

            await Task.Delay(PollInterval, timeoutCts.Token);
        }
    }

    // Mirrors AspireDemo.Api's order contracts (see OrderModels.cs, Program.cs) for
    // (de)serializing requests/responses in this test project, which doesn't reference the Api
    // project directly - these are independently deployable services that only agree on the wire
    // shape.
    private sealed record OrderItemDto(string Sku, int Quantity);

    private sealed record OrderRequestDto(IReadOnlyList<OrderItemDto> Items, decimal Amount, string DestinationPostalCode);

    private sealed record CreateOrderResponseDto(string OrderId);

    private sealed record ShippingQuoteDto(string Carrier, decimal Cost, int EstimatedDays);

    private sealed record OrderResultDto(string OrderId, string Status, ShippingQuoteDto? Quote, string? InvoiceBlobName, string? FailureReason);

    private sealed record OrderStatusResponseDto(string RuntimeStatus, OrderResultDto? Result);
}

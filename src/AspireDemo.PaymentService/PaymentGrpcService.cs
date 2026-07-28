using System.Globalization;
using AspireDemo.PaymentGrpc;
using Dapr.Client;
using Grpc.Core;

namespace AspireDemo.PaymentService;

/// <summary>
/// gRPC implementation of the payment service, reached via Dapr gRPC proxying (see payment.proto).
/// Same behaviour as the previous /charge and /refund minimal APIs: charge is idempotent (the
/// result is cached in the Dapr state store under <c>payment:{orderId}</c>), and orders at or above
/// <see cref="DeclineThreshold"/> are declined so the saga's compensation path has something to
/// exercise. Only genuine infrastructure failures should surface as an RPC error - a decline is a
/// successful call that returns <c>success = false</c>.
/// </summary>
public sealed class PaymentGrpcService(DaprClient dapr) : Payment.PaymentBase
{
    private const string StateStore = "statestore";

    // Demo failure trigger: orders at or above this amount are declined, so the saga's compensation
    // path (release inventory) has something to exercise end-to-end.
    private const decimal DeclineThreshold = 100_000m;

    public override async Task<ChargeReply> Charge(ChargeRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var key = $"payment:{request.OrderId}";

        var existing = await dapr.GetStateAsync<PaymentResult>(StateStore, key, cancellationToken: cancellationToken);
        if (existing is not null)
        {
            return ToReply(existing);
        }

        var amount = decimal.Parse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture);

        var result = amount > 0 && amount < DeclineThreshold
            ? new PaymentResult(true, Guid.NewGuid().ToString("N"), Reason: null)
            : new PaymentResult(false, TransactionId: null, Reason: "declined");

        await dapr.SaveStateAsync(StateStore, key, result, cancellationToken: cancellationToken);
        return ToReply(result);
    }

    public override async Task<RefundReply> Refund(RefundRequest request, ServerCallContext context)
    {
        await dapr.DeleteStateAsync(StateStore, $"payment:{request.OrderId}", cancellationToken: context.CancellationToken);
        return new RefundReply();
    }

    private static ChargeReply ToReply(PaymentResult result) => new()
    {
        Success = result.Success,
        // Proto scalar strings are non-nullable; map the record's nullable fields to empty strings.
        TransactionId = result.TransactionId ?? string.Empty,
        Reason = result.Reason ?? string.Empty
    };

    // Persisted charge result. Kept as a plain record (not the proto message) so the cached state
    // shape stays stable and independent of the wire contract.
    private sealed record PaymentResult(bool Success, string? TransactionId, string? Reason);
}

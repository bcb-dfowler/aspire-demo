using AspireDemo.PaymentGrpc;
using Dapr.Workflow;
using Grpc.Core;

namespace AspireDemo.Api;

public sealed record RefundPaymentRequest(string OrderId);

/// <summary>
/// Compensating activity: refunds a previously charged payment via a gRPC call to payment-svc,
/// routed through Dapr gRPC proxying. Best-effort - logs rather than throws, since compensation
/// itself should not fail the saga.
/// </summary>
public sealed class RefundPaymentActivity(Payment.PaymentClient paymentClient, ILogger<RefundPaymentActivity> logger) : WorkflowActivity<RefundPaymentRequest, object?>
{
    // Instructs the local Dapr sidecar to proxy this gRPC call to the "payment-svc" app.
    private static readonly Metadata DaprInvocationHeaders = new() { { "dapr-app-id", "payment-svc" } };

    public override async Task<object?> RunAsync(WorkflowActivityContext context, RefundPaymentRequest input)
    {
        try
        {
            await paymentClient.RefundAsync(new RefundRequest { OrderId = input.OrderId }, DaprInvocationHeaders);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Refunding payment for order {OrderId} failed", input.OrderId);
        }

        return null;
    }
}

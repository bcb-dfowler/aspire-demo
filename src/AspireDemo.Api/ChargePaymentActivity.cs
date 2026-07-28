using System.Globalization;
using AspireDemo.PaymentGrpc;
using Dapr.Workflow;
using Grpc.Core;

namespace AspireDemo.Api;

public sealed record ChargePaymentRequest(string OrderId, decimal Amount);

public sealed record PaymentResult(bool Success, string? TransactionId, string? Reason);

/// <summary>
/// Charges payment via a gRPC call to payment-svc, routed through Dapr gRPC proxying (the
/// "dapr-app-id" header tells this app's sidecar which app to forward to). Throws on decline so the
/// orchestrator's catch block can run compensation.
/// </summary>
public sealed class ChargePaymentActivity(Payment.PaymentClient paymentClient) : WorkflowActivity<ChargePaymentRequest, PaymentResult>
{
    // Instructs the local Dapr sidecar to proxy this gRPC call to the "payment-svc" app.
    private static readonly Metadata DaprInvocationHeaders = new() { { "dapr-app-id", "payment-svc" } };

    public override async Task<PaymentResult> RunAsync(WorkflowActivityContext context, ChargePaymentRequest input)
    {
        var reply = await paymentClient.ChargeAsync(
            new ChargeRequest
            {
                OrderId = input.OrderId,
                // decimal has no protobuf type; send an invariant-culture string (see payment.proto).
                Amount = input.Amount.ToString(CultureInfo.InvariantCulture)
            },
            DaprInvocationHeaders);

        if (!reply.Success)
        {
            throw new InvalidOperationException($"Payment charge failed for order {input.OrderId}: {reply.Reason}");
        }

        // Map back to the workflow-facing record so activity I/O stays plain-JSON serializable and
        // independent of the gRPC types.
        return new PaymentResult(reply.Success, reply.TransactionId, string.IsNullOrEmpty(reply.Reason) ? null : reply.Reason);
    }
}

using Dapr.Client;
using Dapr.Workflow;

namespace AspireDemo.Api;

public sealed record ChargePaymentRequest(string OrderId, decimal Amount);

public sealed record PaymentResult(bool Success, string? TransactionId, string? Reason);

/// <summary>
/// Charges payment via Dapr service invocation to payment-svc. Throws on decline so the
/// orchestrator's catch block can run compensation.
/// </summary>
public sealed class ChargePaymentActivity(DaprClient daprClient) : WorkflowActivity<ChargePaymentRequest, PaymentResult>
{
    public override async Task<PaymentResult> RunAsync(WorkflowActivityContext context, ChargePaymentRequest input)
    {
        // Demonstrates Dapr service invocation (obsolete in favor of a native HTTP/gRPC client per
        // the SDK's guidance, but still the most direct way to show this building block off).
#pragma warning disable CS0618
        var result = await daprClient.InvokeMethodAsync<ChargePaymentRequest, PaymentResult>(
            HttpMethod.Post, "payment-svc", "charge", input);
#pragma warning restore CS0618

        if (!result.Success)
        {
            throw new InvalidOperationException($"Payment charge failed for order {input.OrderId}: {result.Reason}");
        }

        return result;
    }
}

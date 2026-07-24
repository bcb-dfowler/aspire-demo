using Dapr.Client;
using Dapr.Workflow;

namespace AspireDemo.Api;

public sealed record RefundPaymentRequest(string OrderId);

/// <summary>
/// Compensating activity: refunds a previously charged payment via Dapr service invocation to
/// payment-svc. Best-effort - logs rather than throws, since compensation itself should not fail
/// the saga.
/// </summary>
public sealed class RefundPaymentActivity(DaprClient daprClient, ILogger<RefundPaymentActivity> logger) : WorkflowActivity<RefundPaymentRequest, object?>
{
    public override async Task<object?> RunAsync(WorkflowActivityContext context, RefundPaymentRequest input)
    {
        try
        {
#pragma warning disable CS0618
            await daprClient.InvokeMethodAsync(HttpMethod.Post, "payment-svc", "refund", input);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Refunding payment for order {OrderId} failed", input.OrderId);
        }

        return null;
    }
}

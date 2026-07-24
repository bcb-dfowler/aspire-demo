namespace AspireDemo.PaymentService;

// Mirrors order-svc's ChargePaymentRequest/PaymentResult/RefundPaymentRequest contract (see
// AspireDemo.Api/ChargePaymentActivity.cs and RefundPaymentActivity.cs), without referencing the
// Api project directly - these are independently deployable services that only agree on the wire
// shape. Reached via Dapr service invocation, not a raw sidecar call, so charge/refund always
// return 200 with a result payload; only genuine infrastructure errors should surface as HTTP 5xx.
public sealed record ChargeRequest(string OrderId, decimal Amount);

public sealed record PaymentResult(bool Success, string? TransactionId, string? Reason);

public sealed record RefundRequest(string OrderId);

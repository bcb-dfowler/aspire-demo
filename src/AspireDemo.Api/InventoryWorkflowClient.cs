using System.Text.Json.Serialization;
using Dapr.Client;

namespace AspireDemo.Api;

public sealed record InventoryWorkflowResult(bool Success, string? Reason);

/// <summary>
/// inventory-svc has no HTTP app-channel of its own - it's a plain console app that only talks
/// to its Dapr sidecar (workflow + state). So instead of Dapr service invocation, order-svc talks
/// directly to inventory-svc's *sidecar* HTTP API to start/await a workflow instance there, then
/// reads the outcome back from the shared Dapr state store (which inventory-svc's workflow writes
/// to as its last activity). This is Dapr's documented "multi-app workflow" pattern - each app's
/// workflow engine is reachable at its own sidecar, not just its own app-channel.
/// </summary>
public sealed class InventoryWorkflowClient(HttpClient httpClient, DaprClient daprClient, ILogger<InventoryWorkflowClient> logger)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan WorkflowTimeout = TimeSpan.FromSeconds(30);

    public async Task<InventoryWorkflowResult> RunWorkflowAsync<TInput>(string workflowName, string orderId, TInput input)
    {
        var instanceId = $"{workflowName}-{orderId}";

        using var timeoutCts = new CancellationTokenSource(WorkflowTimeout);

        using var startResponse = await httpClient.PostAsJsonAsync(
            $"/v1.0/workflows/dapr/{workflowName}/start?instanceID={instanceId}", input, timeoutCts.Token);
        startResponse.EnsureSuccessStatusCode();

        while (true)
        {
            var status = await httpClient.GetFromJsonAsync<WorkflowStatusResponse>(
                $"/v1.0/workflows/dapr/{instanceId}", timeoutCts.Token);

            switch (status?.RuntimeStatus)
            {
                case "COMPLETED":
                    var result = await daprClient.GetStateAsync<InventoryWorkflowResult>(
                        "statestore", $"workflow-result:{instanceId}", cancellationToken: timeoutCts.Token);
                    return result ?? new InventoryWorkflowResult(false, "inventory-svc workflow completed with no recorded result");

                case "FAILED" or "TERMINATED":
                    logger.LogWarning("inventory-svc workflow {InstanceId} ended as {RuntimeStatus}", instanceId, status.RuntimeStatus);
                    return new InventoryWorkflowResult(false, status.RuntimeStatus);

                default:
                    await Task.Delay(PollInterval, timeoutCts.Token);
                    break;
            }
        }
    }

    private sealed record WorkflowStatusResponse([property: JsonPropertyName("runtimeStatus")] string? RuntimeStatus);
}

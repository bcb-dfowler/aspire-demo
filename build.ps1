# Publishes the container image for every .NET service in this repo, using the .NET SDK's
# built-in container build (Microsoft.NET.Build.Containers) - no Dockerfile. Run this after
# changing any service's code, before `aspire run` (container-parity mode) or `dotnet test`.
$ErrorActionPreference = "Stop"

$services = @(
    "src/AspireDemo.Api",
    "src/AspireDemo.InventoryService",
    "src/AspireDemo.PaymentService",
    "src/AspireDemo.AnalyticsService",
    "src/AspireDemo.NotificationService"
)

foreach ($service in $services) {
    Write-Host "Publishing container image for $service..." -ForegroundColor Cyan
    dotnet publish $service -c Release --os linux --arch x64 -t:PublishContainer
}

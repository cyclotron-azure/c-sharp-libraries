using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Models;

namespace Cyclotron.Graph.Sample;

/// <summary>
/// Implements both notification sinks by logging the fields each notification carries, and nothing
/// else.
/// </summary>
/// <remarks>
/// This is the seam where a real application applies its own persistence, queueing, retry, and
/// telemetry policy — the library deliberately owns none of those decisions. It validates the
/// clientState and filters by change type before calling in here; everything after that point is
/// the consuming application's responsibility.
/// </remarks>
internal sealed class LoggingNotificationSinks(ILogger<LoggingNotificationSinks> logger)
    : IGraphMessageNotificationSink, IGraphLifecycleNotificationSink
{
    public Task OnResourceChangedAsync(GraphChangeNotification notification, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Change notification: subscriptionId={SubscriptionId} changeType={ChangeType} resource={Resource} resourceId={ResourceId}",
            notification.SubscriptionId,
            notification.ChangeType,
            notification.Resource,
            notification.ResourceData?.Id);

        return Task.CompletedTask;
    }

    public Task OnReauthorizationRequiredAsync(string subscriptionId, CancellationToken ct = default)
    {
        logger.LogInformation("Lifecycle: reauthorizationRequired for subscriptionId={SubscriptionId}", subscriptionId);

        return Task.CompletedTask;
    }

    public Task OnSubscriptionRemovedAsync(string subscriptionId, CancellationToken ct = default)
    {
        logger.LogInformation("Lifecycle: subscriptionRemoved for subscriptionId={SubscriptionId}", subscriptionId);

        return Task.CompletedTask;
    }

    public Task OnMissedAsync(string subscriptionId, CancellationToken ct = default)
    {
        logger.LogInformation("Lifecycle: missed for subscriptionId={SubscriptionId}", subscriptionId);

        return Task.CompletedTask;
    }
}

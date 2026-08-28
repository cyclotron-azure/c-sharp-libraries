using System.Text.Json;
using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Models;
using Cyclotron.Graph.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Notifications;

/// <summary>
/// Deserializes, validates, and filters incoming Graph notification payloads, dispatching each
/// surviving notification to a caller-supplied sink. Once a notification reaches a sink call, any
/// exception the sink throws propagates uncaught — from that point on, error handling is the
/// consuming application's responsibility.
/// </summary>
internal sealed class GraphNotificationParser(
    IGraphNotificationValidator validator,
    IOptions<GraphCoreOptions> options,
    ILogger<GraphNotificationParser> logger) : IGraphNotificationParser
{
    public async Task DispatchMessageNotificationsAsync(string body, IGraphMessageNotificationSink sink, CancellationToken ct = default)
    {
        GraphChangeNotificationCollection? notifications;
        try
        {
            notifications = JsonSerializer.Deserialize<GraphChangeNotificationCollection>(body, GraphNotificationJson.Options);
        }
        catch (JsonException ex)
        {
            // Unrecoverable payload — never succeeds on retry. Throw so the caller's queue trigger
            // abandons the message; it dead-letters at the queue's MaxDeliveryCount.
            logger.LogError(ex, "Failed to deserialize Graph change notification payload: {Body}", body);
            throw;
        }

        if (notifications?.Value is not { Count: > 0 } items)
        {
            return;
        }

        logger.LogDebug("Processing {NotificationCount} Graph change notifications", items.Count);

        foreach (var notification in items)
        {
            if (!validator.IsValid(notification.ClientState))
            {
                logger.LogWarning(
                    "Notification rejected: invalid clientState for subscriptionId={SubscriptionId}",
                    notification.SubscriptionId);
                continue;
            }

            // Null-safe mask test: ChangeType is nullable, and C#'s lifted `&` makes `null & mask`
            // evaluate to null (never zero), so a bare `(ct & mask) == 0` guard would not skip a
            // null change type. Skip both a null change type and one outside the configured mask.
            if (notification.ChangeType is not { } changeType
                || (changeType & options.Value.Subscription.ChangeTypes) == 0)
            {
                logger.LogDebug(
                    "Ignoring notification; invalid change type: SubscriptionId={SubscriptionId} ChangeType={ChangeType}",
                    notification.SubscriptionId, notification.ChangeType);
                continue;
            }

            // ResourceData is nullable because lifecycle notifications omit it. The change-type
            // filter above is caller-configurable, so it can no longer be relied on to exclude
            // resource-less payloads — null-check explicitly instead of dereferencing.
            if (notification.ResourceData is null)
            {
                logger.LogWarning(
                    "Notification rejected: missing resourceData for subscriptionId={SubscriptionId}",
                    notification.SubscriptionId);
                continue;
            }

            await sink.OnResourceChangedAsync(notification, ct);
        }
    }

    public async Task DispatchLifecycleNotificationsAsync(string body, IGraphLifecycleNotificationSink sink, CancellationToken ct = default)
    {
        GraphChangeNotificationCollection? notifications;
        try
        {
            notifications = JsonSerializer.Deserialize<GraphChangeNotificationCollection>(body, GraphNotificationJson.Options);
        }
        catch (JsonException ex)
        {
            // Unrecoverable payload — never succeeds on retry. Throw so the caller's queue trigger
            // abandons the message; it dead-letters at the queue's MaxDeliveryCount.
            logger.LogError(ex, "Failed to deserialize Graph lifecycle notification payload: {Payload}", body);
            throw;
        }

        if (notifications?.Value is not { Count: > 0 } items)
        {
            return;
        }

        foreach (var notification in items)
        {
            var subscriptionId = notification.SubscriptionId;

            if (!validator.IsValid(notification.ClientState))
            {
                logger.LogError("Lifecycle notification rejected: invalid clientState");
                continue;
            }

            logger.LogInformation("Handling lifecycle event={LifecycleEvent}", notification.LifecycleEvent);

            switch (notification.LifecycleEvent)
            {
                case GraphLifecycleEventType.ReauthorizationRequired:
                    await sink.OnReauthorizationRequiredAsync(subscriptionId, ct);
                    break;

                case GraphLifecycleEventType.SubscriptionRemoved:
                    await sink.OnSubscriptionRemovedAsync(subscriptionId, ct);
                    break;

                case GraphLifecycleEventType.Missed:
                    await sink.OnMissedAsync(subscriptionId, ct);
                    break;

                default:
                    logger.LogError("Unknown lifecycle event type={LifecycleEvent}", notification.LifecycleEvent);
                    break;
            }
        }
    }
}

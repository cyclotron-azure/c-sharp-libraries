namespace Cyclotron.Graph.Core.Abstractions;

/// <summary>
/// Implemented by the consuming application to receive Graph subscription lifecycle events.
/// </summary>
public interface IGraphLifecycleNotificationSink
{
    /// <summary>Called when Graph reports that the subscription requires reauthorization to keep running.</summary>
    /// <param name="subscriptionId">The Graph subscription id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnReauthorizationRequiredAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>Called when Graph reports that the subscription was removed.</summary>
    /// <param name="subscriptionId">The Graph subscription id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnSubscriptionRemovedAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>Called when Graph reports that it may have missed delivering one or more change notifications.</summary>
    /// <param name="subscriptionId">The Graph subscription id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnMissedAsync(string subscriptionId, CancellationToken ct = default);
}

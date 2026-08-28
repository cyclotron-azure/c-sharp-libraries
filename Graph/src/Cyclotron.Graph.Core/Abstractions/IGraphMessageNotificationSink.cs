using Cyclotron.Graph.Core.Models;

namespace Cyclotron.Graph.Core.Abstractions;

/// <summary>
/// Implemented by the consuming application to receive Graph change notifications about a
/// resource. "Message" here is Graph's own term for its non-lifecycle change-notification
/// stream — a notification that a resource was created, updated, or deleted — and does not imply
/// an email message; a subscription over any Graph resource type delivers through this same
/// shape.
/// </summary>
public interface IGraphMessageNotificationSink
{
    /// <summary>
    /// Called once per change notification after the library has already validated its
    /// <c>clientState</c> and filtered it against the subscription's configured change types.
    /// The application owns all persistence, queueing, and telemetry decisions from this point on.
    /// </summary>
    /// <param name="notification">The validated, filtered change notification.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnResourceChangedAsync(GraphChangeNotification notification, CancellationToken ct = default);
}

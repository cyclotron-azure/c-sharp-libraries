using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Models;

namespace Cyclotron.Graph.Core.Tests.TestSupport;

/// <summary>
/// Records every change notification dispatched to it, so tests assert on what the parser actually
/// delivered rather than merely on the absence of an exception. Optionally throws, to prove the
/// parser lets a sink exception propagate.
/// </summary>
internal sealed class RecordingMessageSink : IGraphMessageNotificationSink
{
    /// <summary>Every notification dispatched to this sink, in order.</summary>
    public List<GraphChangeNotification> Received { get; } = [];

    /// <summary>When set, thrown on every dispatch instead of recording it.</summary>
    public Exception? ThrowOnDispatch { get; init; }

    public Task OnResourceChangedAsync(GraphChangeNotification notification, CancellationToken ct = default)
    {
        if (ThrowOnDispatch is not null)
        {
            throw ThrowOnDispatch;
        }

        Received.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>The lifecycle sink methods a <see cref="RecordingLifecycleSink"/> can observe.</summary>
public enum LifecycleSinkMethod
{
    /// <summary><see cref="IGraphLifecycleNotificationSink.OnReauthorizationRequiredAsync"/>.</summary>
    ReauthorizationRequired,

    /// <summary><see cref="IGraphLifecycleNotificationSink.OnSubscriptionRemovedAsync"/>.</summary>
    SubscriptionRemoved,

    /// <summary><see cref="IGraphLifecycleNotificationSink.OnMissedAsync"/>.</summary>
    Missed,
}

/// <summary>One recorded lifecycle dispatch: which method ran, and for which subscription.</summary>
public sealed record LifecycleCall(LifecycleSinkMethod Method, string SubscriptionId);

/// <summary>
/// Records which lifecycle sink method was invoked and with which subscription id, so a test can
/// assert on routing rather than on "something happened".
/// </summary>
public sealed class RecordingLifecycleSink : IGraphLifecycleNotificationSink
{
    /// <summary>Every lifecycle dispatch this sink observed, in order.</summary>
    public List<LifecycleCall> Received { get; } = [];

    /// <summary>When set, thrown on every dispatch instead of recording it.</summary>
    public Exception? ThrowOnDispatch { get; init; }

    public Task OnReauthorizationRequiredAsync(string subscriptionId, CancellationToken ct = default) =>
        Record(LifecycleSinkMethod.ReauthorizationRequired, subscriptionId);

    public Task OnSubscriptionRemovedAsync(string subscriptionId, CancellationToken ct = default) =>
        Record(LifecycleSinkMethod.SubscriptionRemoved, subscriptionId);

    public Task OnMissedAsync(string subscriptionId, CancellationToken ct = default) =>
        Record(LifecycleSinkMethod.Missed, subscriptionId);

    private Task Record(LifecycleSinkMethod method, string subscriptionId)
    {
        if (ThrowOnDispatch is not null)
        {
            throw ThrowOnDispatch;
        }

        Received.Add(new LifecycleCall(method, subscriptionId));
        return Task.CompletedTask;
    }
}

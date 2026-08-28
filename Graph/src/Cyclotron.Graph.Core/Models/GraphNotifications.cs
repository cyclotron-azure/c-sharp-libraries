namespace Cyclotron.Graph.Core.Models;

/// <summary>
/// The envelope Graph posts to a change-notification webhook: a batch of one or more
/// notifications delivered together.
/// </summary>
public sealed record GraphChangeNotificationCollection
{
    /// <summary>The notifications in this delivery batch.</summary>
    public List<GraphChangeNotification>? Value { get; init; }
}

/// <summary>
/// A single Graph change notification about a resource, or a lifecycle event about a
/// subscription. Both notification shapes deserialize into this same record; which fields are
/// populated depends on which kind of notification it is.
/// </summary>
public sealed record GraphChangeNotification
{
    /// <summary>The Graph subscription id this notification was delivered for.</summary>
    public required string SubscriptionId { get; init; }

    /// <summary>The change type the notified resource underwent, when this is a change notification.</summary>
    public GraphChangeType? ChangeType { get; init; }

    /// <summary>
    /// Present only for change notifications. Lifecycle notifications
    /// (reauthorizationRequired/subscriptionRemoved/missed) carry no resource,
    /// so this is nullable rather than required.
    /// </summary>
    public string? Resource { get; init; }

    /// <summary>
    /// The clientState is a string that was provided when the subscription
    /// was created. It is used to validate that the notification is from a
    /// trusted source.
    /// </summary>
    /// <remarks>
    /// It will not be null if subscribed with it!
    /// </remarks>
    public string? ClientState { get; init; }

    /// <summary>
    /// Present for change notifications, but absent on lifecycle
    /// notifications (which carry no resource). Nullable so lifecycle payloads
    /// deserialize; the change-notification path null-checks before use.
    /// </summary>
    public GraphChangeNotificationResourceData? ResourceData { get; init; }

    /// <summary>The lifecycle event this notification carries, when this is a lifecycle notification.</summary>
    public GraphLifecycleEventType? LifecycleEvent { get; init; }
}

/// <summary>
/// The minimal resource identity Graph includes on a change notification.
/// </summary>
public sealed record GraphChangeNotificationResourceData
{
    /// <summary>The Graph id of the changed resource.</summary>
    public required string Id { get; init; }
}

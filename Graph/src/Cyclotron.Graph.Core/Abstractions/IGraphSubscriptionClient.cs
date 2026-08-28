using Cyclotron.Graph.Core.Models;

namespace Cyclotron.Graph.Core.Abstractions;

/// <summary>
/// Creates, queries, renews, and deletes Graph change-notification subscriptions. Resource-agnostic:
/// every member takes a caller-supplied resource id and never assumes the resource is mail, which
/// is what lets a future Graph resource family (e.g. Teams, calendar) build on this interface
/// unchanged.
/// </summary>
public interface IGraphSubscriptionClient
{
    /// <summary>
    /// Creates a Graph change-notification subscription for the given resource. Builds the
    /// resource path from <see cref="Options.GraphSubscriptionOptions.ResourceTemplate"/> with
    /// its <c>{resourceId}</c> token substituted by <paramref name="resourceId"/>. Throws on any
    /// Graph failure — wrap the call if you need to tolerate errors.
    /// </summary>
    /// <param name="resourceId">The caller-supplied resource identifier to subscribe to.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<GraphSubscriptionInfo> SubscribeAsync(string resourceId, CancellationToken ct = default);

    /// <summary>
    /// Returns the raw resource-path segment of every active subscription that covers at least
    /// one of <paramref name="changeTypes"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="GraphChangeType.None"/> means no filtering is applied (preserving today's
    /// null-means-all semantics) — every active subscription's resource segment is returned.
    /// The returned strings are the raw resource-path segment of each matching subscription, not
    /// necessarily an id: during a migration between identifier forms, this set can contain
    /// values in an older form (for a mail consumer, an address rather than an id) for
    /// subscriptions created before the caller migrated. A caller reconciling its own state
    /// against this set should test membership for both the old and new forms until those older
    /// subscriptions have rolled over.
    /// </remarks>
    /// <param name="changeTypes">The change types to match, or <see cref="GraphChangeType.None"/> for no filtering.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<HashSet<string>> GetActiveSubscribedResourceIdsAsync(
        GraphChangeType changeTypes = GraphChangeType.None, CancellationToken ct = default);

    /// <summary>
    /// Returns the subscription with the given Graph subscription id, or null if not found.
    /// </summary>
    /// <param name="subscriptionId">The Graph subscription id to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<GraphSubscriptionInfo?> GetSubscriptionByIdAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the active subscription whose resource-path segment matches <paramref name="resourceId"/>,
    /// or null if none exists. Use when there is no stored subscription id to detect a
    /// subscription that already exists in Graph and avoid creating a duplicate.
    /// </summary>
    /// <param name="resourceId">The resource identifier to match against each subscription's resource path.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<GraphSubscriptionInfo?> GetSubscriptionByResourceIdAsync(string resourceId, CancellationToken ct = default);

    /// <summary>
    /// Returns the active subscription whose resource-path segment equals any of
    /// <paramref name="resourceIdentifiers"/>, matched case-insensitively.
    /// </summary>
    /// <remarks>
    /// This overload exists solely to match subscriptions created before a caller migrated its
    /// identifier form. Every subscription this library creates uses the identifier the caller
    /// supplied to <see cref="SubscribeAsync"/> — this method never needs more than one
    /// identifier to match a subscription this library created. A caller migrating from an
    /// older, address-keyed identifier form passes both its old and new identifiers (e.g.
    /// <c>[id, address]</c>); a greenfield caller with no legacy subscriptions should prefer
    /// <see cref="GetSubscriptionByResourceIdAsync"/> instead. This overload can be dropped once
    /// a tenant's subscriptions have rolled over to the current identifier form.
    /// </remarks>
    /// <param name="resourceIdentifiers">The set of identifiers to match against each subscription's resource path.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<GraphSubscriptionInfo?> GetSubscriptionByResourceIdentifiersAsync(
        IReadOnlyCollection<string> resourceIdentifiers, CancellationToken ct = default);

    /// <summary>
    /// Extends the expiry of an existing Graph subscription. Returns the new expiry as confirmed
    /// by Graph.
    /// </summary>
    /// <param name="subscriptionId">The Graph subscription id to renew.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DateTimeOffset> RenewSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a Graph subscription. Tolerates a 404 (subscription already gone).
    /// </summary>
    /// <param name="subscriptionId">The Graph subscription id to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UnsubscribeAsync(string subscriptionId, CancellationToken ct = default);
}

using Cyclotron.Graph.Core.Models;

namespace Cyclotron.Graph.Core.Client;

/// <summary>
/// Maps Graph subscription DTOs onto the public <see cref="GraphSubscriptionInfo"/> model.
/// Lives alongside <see cref="SubscriptionDto"/> since both are internal wire-shaped types the
/// public surface must not reference.
/// </summary>
internal static class GraphSubscriptionMapping
{
    /// <summary>
    /// Extracts the resource-path segment from a subscription's resource path, URL-decoding it,
    /// or null if the resource is absent or not in the expected shape. The resource path is
    /// "users/{segment}/messages" (or a caller-configured equivalent) — the segment is whatever
    /// identifier the subscription was created with. For a mail consumer using this library that
    /// is the Graph user <c>id</c>, though a subscription created before a caller migrated
    /// identifier forms may hold an address instead.
    /// </summary>
    public static string? ParseResourceId(this SubscriptionDto subscription) =>
        subscription.Resource?.Split('/') is [_, string segment, ..] && !string.IsNullOrEmpty(segment)
            ? Uri.UnescapeDataString(segment)
            : null;

    /// <summary>
    /// Projects a single Graph subscription DTO to <see cref="GraphSubscriptionInfo"/>,
    /// or null if the DTO is missing an id.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The DTO carries no recognizable <c>changeType</c>. An absent id means "no subscription
    /// here", which callers handle as a null result; an absent change type means the subscription
    /// exists but Graph broke its contract, which callers cannot meaningfully handle. Hence null
    /// for the former and a throw for the latter.
    /// </exception>
    public static GraphSubscriptionInfo? MapFrom(SubscriptionDto subscription)
    {
        if (string.IsNullOrEmpty(subscription.Id))
        {
            return null;
        }

        // ChangeTypes parses Graph's comma-separated changeType into a non-nullable flags enum, so
        // an omitted (or wholly unrecognized) changeType collapses to None -- indistinguishable
        // from a real value. Mapping that through would describe the subscription as covering no
        // change types at all, and a caller filtering by change type would then skip a
        // subscription that does in fact cover it. Mirrors the ExpirationDateTime guard in
        // GraphSubscriptionClient: surface the contract violation rather than guess.
        if (subscription.ChangeTypes == GraphChangeType.None)
        {
            throw new InvalidOperationException(
                $"Graph subscription response missing changeType for subscriptionId {subscription.Id}");
        }

        return new GraphSubscriptionInfo(
            subscription.Id, subscription.ParseResourceId(), subscription.ChangeTypes, subscription.ExpirationDateTime);
    }
}

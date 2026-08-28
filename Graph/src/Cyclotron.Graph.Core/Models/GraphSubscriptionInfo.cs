namespace Cyclotron.Graph.Core.Models;

/// <summary>
/// What a successful Graph subscribe call returns to callers. Lets a caller persist the
/// subscription pointer alongside its own state so a future renew or unsubscribe can target the
/// exact subscription by id, without re-querying Graph and matching by resource.
/// </summary>
/// <param name="Id">The Graph subscription id.</param>
/// <param name="ResourceId">
/// The resource-path segment the subscription was created against — the raw value that was
/// substituted into <see cref="Options.GraphSubscriptionOptions.ResourceTemplate"/>'s
/// <c>{resourceId}</c> token. For a mail subscription created via <c>Cyclotron.Graph.Mail</c>,
/// this holds the Graph user <c>id</c>. Nullable because a subscription retrieved from Graph
/// may not always surface its resource path in a form this library can extract.
/// </param>
/// <param name="ChangeTypes">The change types the subscription covers.</param>
/// <param name="ExpiresAt">When the subscription expires, as reported by Graph.</param>
public sealed record GraphSubscriptionInfo(
    string Id,
    string? ResourceId,
    GraphChangeType ChangeTypes,
    DateTimeOffset ExpiresAt);

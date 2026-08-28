using Cyclotron.Graph.Mail.Models;

namespace Cyclotron.Graph.Mail.Abstractions;

/// <summary>
/// Fetches and updates Graph mail messages and their well-known folders. Every member except
/// <see cref="ResolveMailboxIdAsync"/> takes a <c>mailboxId</c> — the Graph user <c>id</c>, not
/// an email address.
/// </summary>
/// <remarks>
/// Subscription operations (subscribe, renew, unsubscribe, and subscription lookup) are not part
/// of this interface — they are resource-agnostic and live on
/// <see cref="Cyclotron.Graph.Core.Abstractions.IGraphSubscriptionClient"/>. A mail consumer
/// resolves both interfaces to get the full set of mail capabilities; this interface will never
/// grow a <c>SubscribeAsync</c> or similar member.
/// </remarks>
public interface IGraphMailClient
{
    /// <summary>
    /// Resolves the Graph user <c>id</c> for a mailbox email address. Returns null if the user
    /// cannot be found.
    /// </summary>
    /// <param name="mailboxAddress">
    /// The mailbox email address. This is the only method on this interface that takes an
    /// address rather than a mailboxId — every other member takes the resolved id returned here.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> ResolveMailboxIdAsync(string mailboxAddress, CancellationToken ct = default);

    /// <summary>
    /// Resolves the Graph folder ids of the mailbox's well-known Inbox and Sent Items folders.
    /// </summary>
    /// <param name="mailboxId">
    /// The Graph user <c>id</c> — the <c>id</c> property of the Graph <c>user</c> resource, as
    /// returned by <see cref="ResolveMailboxIdAsync"/>. Not an email address: a mailbox may have
    /// alias addresses, so an address is not a stable key.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<GraphWellKnownFolderIds> GetWellKnownFolderIdsAsync(string mailboxId, CancellationToken ct = default);

    /// <summary>
    /// Fetches a single message from the mailbox. Returns null if the message is not found.
    /// </summary>
    /// <param name="mailboxId">
    /// The Graph user <c>id</c> — the <c>id</c> property of the Graph <c>user</c> resource, as
    /// returned by <see cref="ResolveMailboxIdAsync"/>. Not an email address: a mailbox may have
    /// alias addresses, so an address is not a stable key.
    /// </param>
    /// <param name="messageId">The Graph message id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<GraphMailboxMessage?> GetMessageAsync(string mailboxId, string messageId, CancellationToken ct = default);

    /// <summary>
    /// Fetches a single message and deserializes Graph's response directly into
    /// <typeparamref name="T"/>, using the caller-supplied <paramref name="selectFields"/> as the
    /// Graph <c>$select</c> list. Returns null if the message is not found. Use this overload when
    /// only a subset of fields is needed and the caller has its own projection type.
    /// </summary>
    /// <typeparam name="T">The caller-supplied type to deserialize Graph's response into.</typeparam>
    /// <param name="mailboxId">
    /// The Graph user <c>id</c> — the <c>id</c> property of the Graph <c>user</c> resource, as
    /// returned by <see cref="ResolveMailboxIdAsync"/>. Not an email address: a mailbox may have
    /// alias addresses, so an address is not a stable key.
    /// </param>
    /// <param name="messageId">The Graph message id.</param>
    /// <param name="selectFields">The Graph <c>$select</c> fields to request.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<T?> GetMessageAsync<T>(string mailboxId, string messageId, IReadOnlyList<string> selectFields, CancellationToken ct = default);

    /// <summary>
    /// Replaces the category tags on a message. Throws on any Graph failure.
    /// </summary>
    /// <param name="mailboxId">
    /// The Graph user <c>id</c> — the <c>id</c> property of the Graph <c>user</c> resource, as
    /// returned by <see cref="ResolveMailboxIdAsync"/>. Not an email address: a mailbox may have
    /// alias addresses, so an address is not a stable key.
    /// </param>
    /// <param name="messageId">The Graph message id.</param>
    /// <param name="categories">The full set of categories to write (replaces existing values).</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateMessageCategoriesAsync(string mailboxId, string messageId, IEnumerable<string> categories, CancellationToken ct = default);

    /// <summary>
    /// Pages through the Graph delta query for the mailbox from the stored cursor and returns all
    /// messages received since that point, together with an updated delta link to persist for the
    /// next call.
    /// </summary>
    /// <param name="mailboxId">
    /// The Graph user <c>id</c> — the <c>id</c> property of the Graph <c>user</c> resource, as
    /// returned by <see cref="ResolveMailboxIdAsync"/>. Not an email address: a mailbox may have
    /// alias addresses, so an address is not a stable key.
    /// </param>
    /// <param name="deltaLink">
    /// The cursor from the previous call. Pass null to start a full backfill from
    /// <paramref name="floor"/>.
    /// </param>
    /// <param name="floor">
    /// The earliest <c>receivedDateTime</c> to include. Applied on the initial call and on any
    /// 410 restart so the backfill never replays history older than this floor.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<(IReadOnlyList<GraphMailboxMessage> Messages, string DeltaLink)> GetMessagesDeltaAsync(
        string mailboxId, string? deltaLink, DateTimeOffset floor, CancellationToken ct = default);
}

namespace Cyclotron.Graph.Core.Abstractions;

/// <summary>
/// Parses, validates, and filters an incoming Graph notification payload, then dispatches each
/// individual notification to a caller-supplied sink.
/// </summary>
public interface IGraphNotificationParser
{
    /// <summary>
    /// Parses a change-notification webhook payload and, for each notification that passes
    /// <c>clientState</c> validation and change-type filtering, invokes
    /// <paramref name="sink"/>. "Message" here denotes a Graph change notification about a
    /// resource — Graph's own term for this notification stream — not an email.
    /// </summary>
    /// <remarks>
    /// Throws on an unrecoverable payload (e.g. malformed JSON) so the caller's queue trigger can
    /// abandon or dead-letter the message. Returns normally when individual notifications within
    /// an otherwise well-formed payload are legitimately skipped (invalid <c>clientState</c>, a
    /// change type outside the configured filter) — that is not a failure of the dispatch call.
    /// </remarks>
    /// <param name="body">The raw notification payload body.</param>
    /// <param name="sink">The sink to invoke for each notification that passes validation and filtering.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchMessageNotificationsAsync(string body, IGraphMessageNotificationSink sink, CancellationToken ct = default);

    /// <summary>
    /// Parses a lifecycle-notification webhook payload and, for each notification, invokes the
    /// corresponding member on <paramref name="sink"/>.
    /// </summary>
    /// <remarks>
    /// Throws on an unrecoverable payload (e.g. malformed JSON) so the caller's queue trigger can
    /// abandon or dead-letter the message. Returns normally when an individual notification is
    /// legitimately skipped.
    /// </remarks>
    /// <param name="body">The raw notification payload body.</param>
    /// <param name="sink">The sink to invoke for each lifecycle event.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchLifecycleNotificationsAsync(string body, IGraphLifecycleNotificationSink sink, CancellationToken ct = default);
}

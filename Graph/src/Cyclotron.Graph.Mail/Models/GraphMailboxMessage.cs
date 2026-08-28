namespace Cyclotron.Graph.Mail.Models;

/// <summary>
/// A Graph mail message, mapped from the Graph <c>message</c> resource into the shape
/// <see cref="Abstractions.IGraphMailClient"/> returns.
/// </summary>
public sealed class GraphMailboxMessage
{
    /// <summary>
    /// The Graph user <c>id</c> of the mailbox this message was fetched from.
    /// </summary>
    /// <remarks>
    /// Manually set from a parameter after the fetch, not returned by Graph on the message
    /// resource itself, hence the setter.
    /// </remarks>
    public string? MailboxId { get; set; }

    /// <summary>The Graph message id.</summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// The unique id across mailboxes and Graph API calls.
    /// </summary>
    /// <remarks>
    /// This will always be populated when it's not a draft email.
    /// </remarks>
    public string? InternetMessageId { get; init; }

    /// <summary>The message subject.</summary>
    public string? Subject { get; init; }

    /// <summary>The message body content, as selected.</summary>
    public string? Body { get; init; }

    /// <summary>The message's unique body content (the portion added by the sender, minus quoted history), as selected.</summary>
    public string? UniqueBody { get; init; }

    /// <summary>The date and time the message was received.</summary>
    public DateTimeOffset? ReceivedDateTime { get; init; }

    /// <summary>Whether the message has one or more attachments.</summary>
    public bool HasAttachments { get; init; }

    /// <summary>The message sender.</summary>
    public GraphMailboxRecipient? From { get; init; }

    /// <summary>The message's "To" recipients.</summary>
    public IReadOnlyList<GraphMailboxRecipient> ToRecipients { get; init; } = [];

    /// <summary>The content type (e.g. <c>text</c> or <c>html</c>) of <see cref="UniqueBody"/>.</summary>
    public string? ContentType { get; init; }

    /// <summary>The category tags currently applied to the message.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The RFC 5322 <c>In-Reply-To</c> header value, if present.</summary>
    public string? InReplyTo { get; init; }

    /// <summary>
    /// Ancestor message ids from the RFC 5322 <c>References</c> header, ordered most-recent
    /// ancestor first.
    /// </summary>
    public IReadOnlyList<string> References { get; init; } = [];
}

/// <summary>
/// A Graph mail recipient (sender or "To" recipient) — an email address and optional display name.
/// </summary>
public sealed class GraphMailboxRecipient
{
    /// <summary>The recipient's email address.</summary>
    public required string Address { get; init; }

    /// <summary>The recipient's display name, if provided.</summary>
    public string? Name { get; init; }
}

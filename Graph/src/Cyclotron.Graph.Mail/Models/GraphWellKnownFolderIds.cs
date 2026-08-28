namespace Cyclotron.Graph.Mail.Models;

/// <summary>
/// The Graph folder ids of a mailbox's well-known Inbox and Sent Items folders. Either may be
/// null if Graph could not resolve that folder. A consumer typically resolves these once per
/// mailbox and persists them.
/// </summary>
/// <param name="InboxFolderId">The Graph folder id of the mailbox's Inbox, or null if it could not be resolved.</param>
/// <param name="SentItemsFolderId">The Graph folder id of the mailbox's Sent Items folder, or null if it could not be resolved.</param>
public sealed record GraphWellKnownFolderIds(string? InboxFolderId, string? SentItemsFolderId);

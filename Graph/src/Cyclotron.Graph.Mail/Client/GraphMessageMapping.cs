using Cyclotron.Graph.Mail.Models;
using Cyclotron.Graph.Mail.Options;

namespace Cyclotron.Graph.Mail.Client;

/// <summary>
/// Projects a Graph <see cref="MessageDto"/> wire response into the public
/// <see cref="GraphMailboxMessage"/> shape.
/// </summary>
internal static class GraphMessageMapping
{
    public static GraphMailboxMessage MapToModel(MessageDto dto, GraphMessageOptions options) => new()
    {
        MessageId         = dto.Id!,
        InternetMessageId = dto.InternetMessageId,
        Subject           = dto.Subject,
        ReceivedDateTime  = dto.ReceivedDateTime,
        UniqueBody        = dto.UniqueBody?.Content?.Trim(),
        Body              = dto.Body?.Content?.Trim(),
        HasAttachments    = dto.HasAttachments,
        From = dto.From?.EmailAddress is { Address: not null } f
            ? new GraphMailboxRecipient { Address = f.Address, Name = f.Name }
            : null,
        ToRecipients = dto.ToRecipients?
            .Where(r => r.EmailAddress?.Address is not null)
            .Select(r => new GraphMailboxRecipient { Address = r.EmailAddress!.Address!, Name = r.EmailAddress.Name })
            .ToList() ?? [],
        ContentType = dto.UniqueBody?.ContentType,
        Categories = dto.Categories ?? [],
        InReplyTo = dto.Headers
            ?.FirstOrDefault(p => string.Equals(p.Id, options.MapiInReplyToTag, StringComparison.OrdinalIgnoreCase))
            ?.Value,
        References = MessageLineage.ParseReferenceIds(dto.Headers
            ?.FirstOrDefault(p => string.Equals(p.Id, options.MapiReferencesTag, StringComparison.OrdinalIgnoreCase))
            ?.Value),
    };
}

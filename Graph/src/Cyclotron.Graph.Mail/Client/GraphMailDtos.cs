using System.Text.Json.Serialization;

namespace Cyclotron.Graph.Mail.Client;

internal sealed class MessageDto
{
    public string? Id { get; init; }
    public string? InternetMessageId { get; init; }
    public string? Subject { get; init; }
    public DateTimeOffset? ReceivedDateTime { get; init; }
    public BodyDto? UniqueBody { get; init; }
    public BodyDto? Body { get; init; }
    public bool HasAttachments { get; init; }
    public RecipientWrapperDto? From { get; init; }
    public List<RecipientWrapperDto>? ToRecipients { get; init; }
    public List<string>? Categories { get; init; }

    [JsonPropertyName("singleValueExtendedProperties")]
    public List<SingleValueExtendedPropertyDto>? Headers { get; init; }
}

/// <summary>
/// Represents a single-value extended property on a message. This stores
/// the MAPI headers such as PR_IN_REPLY_TO_ID and PR_REFERENCES.
/// </summary>
/// <example>
/// <code>
/// {
///     "singleValueExtendedProperties": [
///        {
///          "id": "String 0x1042",
///          "value": "&lt;CH8PR19MB90451CEFD11925E115766469E81D2@CH8PR19MB9045.namprd19.prod.outlook.com&gt;"
///        },
///        {
///          "id": "String 0x1039",
///          "value": "&lt;CH8PR19MB9045B342C2485C9048E49E11E81D2@CH8PR19MB9045.namprd19.prod.outlook.com&gt; &lt;CH8PR19MB90451CEFD11925E115766469E81D2@CH8PR19MB9045.namprd19.prod.outlook.com&gt;"
///        }
///     ]
/// }
/// </code>
/// </example>
internal sealed class SingleValueExtendedPropertyDto
{
    public string? Id { get; init; }

    public string? Value { get; init; }
}

internal sealed class RecipientWrapperDto
{
    public EmailAddressDto? EmailAddress { get; init; }
}

internal sealed class EmailAddressDto
{
    public string? Address { get; init; }
    public string? Name { get; init; }
}

internal sealed class BodyDto
{
    public string? ContentType { get; init; }
    public string? Content { get; init; }
}

internal sealed class DeltaResponseDto
{
    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; init; }

    [JsonPropertyName("@odata.deltaLink")]
    public string? DeltaLink { get; init; }

    [JsonPropertyName("value")]
    public List<MessageDto>? Messages { get; init; }
}

internal sealed class UserIdResponseDto
{
    public string? Id { get; init; }
}

internal sealed class MailFolderIdResponseDto
{
    public string? Id { get; init; }
}

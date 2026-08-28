using System.Text.Json.Serialization;
using Cyclotron.Graph.Core.Models;

namespace Cyclotron.Graph.Core.Client;

internal sealed class SubscriptionDto
{
    public string? Id { get; init; }
    public string? Resource { get; init; }

    /// <summary>
    /// Raw Graph <c>changeType</c>. On a subscription this is a comma-separated list of the
    /// change types the subscription covers (e.g. "created,updated"), so it cannot be a single
    /// enum value — use <see cref="ChangeTypes"/> for the parsed set.
    /// </summary>
    public string? ChangeType { get; init; }

    public DateTimeOffset ExpirationDateTime { get; init; }

    /// <summary>
    /// The parsed <see cref="GraphChangeType"/> flags value from the comma-separated
    /// <see cref="ChangeType"/> string. Unrecognized tokens are skipped.
    /// <para>
    /// <see cref="GraphChangeType.None"/> here means the field was absent, empty, or carried only
    /// tokens this library does not recognize — never that Graph reported a subscription covering
    /// nothing. Construction sites that project this onto
    /// <see cref="GraphSubscriptionInfo"/> therefore treat <c>None</c> as a contract violation and
    /// throw. Read paths that only filter on this value tolerate it, since one degraded
    /// subscription must not fail a whole-tenant sweep.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public GraphChangeType ChangeTypes => GraphChangeTypeExtensions.Parse(ChangeType);
}

internal sealed class SubscriptionListDto
{
    [JsonPropertyName("value")]
    public List<SubscriptionDto>? Subscriptions { get; init; }
}

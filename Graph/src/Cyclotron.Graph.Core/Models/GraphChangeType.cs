using System.Text.Json.Serialization;

namespace Cyclotron.Graph.Core.Models;

/// <summary>
/// The set of Graph change-notification operations a subscription can cover. Graph's wire
/// representation is a comma-separated string (e.g. <c>"created,updated"</c>); this type
/// combines those into a single flags value so a subscription's coverage can be expressed,
/// stored, and filtered as one value instead of a raw string.
/// </summary>
[Flags]
public enum GraphChangeType
{
    /// <summary>No change types. Also used to mean "no filtering" on methods that accept this value as an optional filter.</summary>
    None = 0,

    /// <summary>A resource was created.</summary>
    Created = 1,

    /// <summary>A resource was updated.</summary>
    Updated = 2,

    /// <summary>A resource was deleted.</summary>
    Deleted = 4,
}

/// <summary>
/// The three lifecycle-notification events Graph can send for a subscription, distinct from
/// change notifications about a resource. Decorated with <see cref="JsonStringEnumConverter"/>
/// because Graph sends these as lowercase-camel-case JSON strings, preserving the deserialization
/// behavior this type replaces.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GraphLifecycleEventType
{
    /// <summary>The subscription requires reauthorization to keep running.</summary>
    ReauthorizationRequired,

    /// <summary>Graph removed the subscription (e.g. it was not renewed before expiry).</summary>
    SubscriptionRemoved,

    /// <summary>Graph may have missed delivering one or more change notifications.</summary>
    Missed,
}

/// <summary>
/// Parses and renders <see cref="GraphChangeType"/> to and from Graph's comma-separated wire
/// form (e.g. <c>"created,updated"</c>). A pure helper with no dependencies, fully implemented
/// here because it has no external dependencies unlike the interfaces in this package.
/// </summary>
public static class GraphChangeTypeExtensions
{
    /// <summary>
    /// Parses Graph's comma-separated <c>changeType</c> string into a single combined
    /// <see cref="GraphChangeType"/> value. Matching is case-insensitive; unrecognized tokens are
    /// skipped rather than throwing, so a payload containing a future Graph change type this
    /// library does not yet know about still parses the tokens it recognizes. A null or empty
    /// input yields <see cref="GraphChangeType.None"/>.
    /// </summary>
    /// <param name="commaSeparated">Graph's comma-separated change-type string, e.g. <c>"created,updated"</c>.</param>
    public static GraphChangeType Parse(string? commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
        {
            return GraphChangeType.None;
        }

        var result = GraphChangeType.None;
        foreach (var token in commaSeparated.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse<GraphChangeType>(token, ignoreCase: true, out var parsed) && parsed != GraphChangeType.None)
            {
                result |= parsed;
            }
        }

        return result;
    }

    /// <summary>
    /// Renders a <see cref="GraphChangeType"/> value back to Graph's lowercase comma-separated
    /// wire form, used when building a subscribe payload's <c>changeType</c> field. Flags are
    /// emitted in declaration order (<c>Created</c>, then <c>Updated</c>, then <c>Deleted</c>).
    /// <see cref="GraphChangeType.None"/> renders as an empty string.
    /// </summary>
    /// <param name="value">The combined change-type value to render.</param>
    public static string ToGraphString(this GraphChangeType value)
    {
        var tokens = new List<string>();

        if (value.HasFlag(GraphChangeType.Created))
        {
            tokens.Add("created");
        }

        if (value.HasFlag(GraphChangeType.Updated))
        {
            tokens.Add("updated");
        }

        if (value.HasFlag(GraphChangeType.Deleted))
        {
            tokens.Add("deleted");
        }

        return string.Join(',', tokens);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cyclotron.Graph.Core.Notifications;

/// <summary>
/// The single shared <see cref="JsonSerializerOptions"/> instance used to deserialize both change
/// and lifecycle notification payloads.
/// </summary>
internal static class GraphNotificationJson
{
    /// <summary>
    /// Case-insensitive property matching plus a camel-case string-enum converter, so Graph's
    /// lowercase-camel-case JSON (e.g. <c>"created"</c>, <c>"reauthorizationRequired"</c>)
    /// deserializes into the corresponding enum members.
    /// </summary>
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

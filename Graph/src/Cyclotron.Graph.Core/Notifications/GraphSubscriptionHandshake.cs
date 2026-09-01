using System.Net;

namespace Cyclotron.Graph.Core.Notifications;

/// <summary>
/// Detects the Graph subscription validation handshake. When a subscription is created (and on
/// lifecycle-URL validation), Graph sends <c>?validationToken=&lt;token&gt;</c> to the notification
/// and lifecycle URLs and requires the endpoint to echo the decoded token back as
/// <c>200 text/plain</c> within 10 seconds — otherwise subscription creation fails. Hosts must
/// check for the handshake before handing the request body to <see cref="GraphNotificationParser"/>,
/// because a validation request carries no notification payload.
/// </summary>
/// <remarks>
/// Framework-neutral on purpose: it takes the raw query string rather than an HTTP abstraction, so
/// the same call works from an ASP.NET Core endpoint (<c>request.QueryString.Value</c>) or an
/// isolated Azure Function (<c>req.Url.Query</c>).
/// </remarks>
public static class GraphSubscriptionHandshake
{
    private const string ValidationTokenParameter = "validationToken";

    /// <summary>
    /// Extracts the Graph validation token from a request's raw query string, if present.
    /// Returns <see langword="true"/> with the percent-decoded token when the request is a
    /// validation handshake; the caller must then respond <c>200 text/plain</c> with
    /// <paramref name="validationToken"/> as the entire body and skip notification dispatch.
    /// Returns <see langword="false"/> for an ordinary notification delivery.
    /// </summary>
    /// <param name="queryString">
    /// The raw (still-encoded) query string, with or without the leading <c>?</c>. Null or empty
    /// means no handshake.
    /// </param>
    /// <param name="validationToken">The decoded token to echo back, or null.</param>
    public static bool TryGetValidationToken(string? queryString, out string? validationToken)
    {
        validationToken = null;

        if (string.IsNullOrEmpty(queryString))
        {
            return false;
        }

        var span = queryString.AsSpan();
        if (span[0] == '?')
        {
            span = span[1..];
        }

        foreach (var pairRange in span.Split('&'))
        {
            var pair = span[pairRange];
            var separator = pair.IndexOf('=');
            var name = separator >= 0 ? pair[..separator] : pair;

            // Decode the name too: a strictly-encoded sender may percent-encode it. Graph sends
            // the parameter camel-cased, but casing is not part of its contract — accept any.
            if (!ValidationTokenParameter.Equals(
                WebUtility.UrlDecode(name.ToString()), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The token contains spaces and colons, so it arrives percent-encoded (Graph) or
            // '+'-encoded (some clients); WebUtility.UrlDecode handles both.
            var value = separator >= 0 ? WebUtility.UrlDecode(pair[(separator + 1)..].ToString()) : string.Empty;
            if (string.IsNullOrEmpty(value))
            {
                // A present-but-empty token is not a handshake Graph would ever send, and echoing
                // an empty body back cannot validate anything — treat it as a normal delivery.
                return false;
            }

            validationToken = value;
            return true;
        }

        return false;
    }
}

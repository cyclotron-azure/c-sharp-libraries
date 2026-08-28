using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cyclotron.Graph.Mail.Models;

/// <summary>
/// Pure helper functions for correlating messages across a reply/forward chain: normalizing a
/// subject for bucket comparison, hashing a normalized subject for storage/lookup, and parsing
/// the RFC 5322 <c>References</c> header into ordered ancestor message ids.
/// </summary>
public static partial class MessageLineage
{
    /// <summary>
    /// Strips a leading reply/forward prefix (<c>Re:</c>, <c>Fw:</c>, <c>Fwd:</c>, case-insensitive)
    /// and surrounding whitespace from a subject, so <c>"RE: Quarterly Review"</c> and
    /// <c>"Quarterly Review"</c> normalize to the same value. Only the leading prefix is stripped —
    /// a subject like <c>"Fwd: RE: Renewal"</c> has just its outermost prefix removed. A null
    /// subject normalizes to an empty string.
    /// </summary>
    /// <param name="subject">The raw subject to normalize, or null.</param>
    public static string NormalizeSubject(string? subject)
    {
        if (subject is null)
        {
            return string.Empty;
        }

        return SubjectPrefixRegex().Replace(subject.Trim(), string.Empty).Trim();
    }

    // SHA-256 first 16 bytes — non-cryptographic use (subject bucket lookup).
    // Truncation is intentional; collision probability is negligible at inbox scale.
    /// <summary>
    /// Computes a 16-byte hash of a normalized subject, suitable for use as a lookup/bucket key.
    /// Callers that persist this output must not change this method's behavior, since a changed
    /// hash silently breaks lookups against previously stored values.
    /// </summary>
    /// <param name="normalizedSubject">A subject already passed through <see cref="NormalizeSubject"/>.</param>
    public static byte[] ComputeSubjectHash(string normalizedSubject)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSubject));
        return hash[..16];
    }

    // Parses the RFC 5322 References header into individual message IDs,
    // reversed so the first element is the most recent ancestor.
    /// <summary>
    /// Parses the RFC 5322 <c>References</c> header into individual message ids, reversed so the
    /// first element is the most recent ancestor. Returns an empty list for a null, empty, or
    /// whitespace-only input.
    /// </summary>
    /// <param name="references">The raw <c>References</c> header value, or null.</param>
    public static IReadOnlyList<string> ParseReferenceIds(string? references)
    {
        if (string.IsNullOrWhiteSpace(references))
        {
            return [];
        }

        return [.. references.Split(' ', StringSplitOptions.RemoveEmptyEntries).Reverse()];
    }

    [GeneratedRegex(@"^(re|fw|fwd)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectPrefixRegex();
}

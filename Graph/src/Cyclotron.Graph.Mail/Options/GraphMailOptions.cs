using System.Collections.Immutable;

namespace Cyclotron.Graph.Mail.Options;

/// <summary>
/// Root configuration for <c>Cyclotron.Graph.Mail</c>: the message <c>$select</c>/MAPI settings
/// and the delta-query folder scope. Bound from the section named by
/// <see cref="DefaultSectionName"/> unless a consumer's DI wiring specifies a different section.
/// </summary>
public sealed class GraphMailOptions
{
    /// <summary>The default configuration section name this options type binds from.</summary>
    public const string DefaultSectionName = "Graph:Mail";

    // Mutable get/set properties, not init-only: this type is bound from configuration via
    // Microsoft.Extensions.Options, which requires a public parameterless constructor and
    // settable properties. The repo-wide "prefer init-only" convention does not apply here.

    /// <summary>Message-fetch configuration.</summary>
    public GraphMessageOptions Message { get; set; } = new();

    /// <summary>Delta-query configuration.</summary>
    public GraphDeltaOptions Delta { get; set; } = new();
}

/// <summary>
/// Configuration for fetching an individual Graph mail message, including the MAPI extended
/// properties used to recover the <c>In-Reply-To</c> and <c>References</c> headers.
/// </summary>
public sealed class GraphMessageOptions
{
    // Mutable get/set properties, not init-only: this type is bound from configuration.

    /// <summary>
    /// The <c>$select</c> fields applied to <see cref="SelectFields"/> when a consumer leaves it
    /// unconfigured - today's fixed field list, preserved here as the fallback rather than as a
    /// property initializer. Declared <see cref="ImmutableArray{T}"/> rather than
    /// <see cref="IReadOnlyList{T}"/> because this single instance is shared by every
    /// unconfigured options object in the process: a read-only <i>interface</i> still exposes a
    /// castable, mutable backing array, so one consumer could corrupt the defaults for all of
    /// them. Immutability by <i>type</i> removes that possibility structurally.
    /// </summary>
    public static readonly ImmutableArray<string> DefaultSelectFields =
    [
        "internetMessageId",
        "subject",
        "receivedDateTime",
        "body",
        "hasAttachments",
        "from",
        "toRecipients",
        "uniqueBody",
        "categories",
    ];

    /// <summary>
    /// The Graph <c>$select</c> fields requested when fetching a message. Defaults to an
    /// <b>empty</b> list, and <see cref="DefaultSelectFields"/> is applied in its place during
    /// registration when this property is left unconfigured. An empty list therefore also means
    /// "apply the defaults": the fallback cannot distinguish an unconfigured list from one a
    /// consumer emptied deliberately, and a mail client with no <c>$select</c> fields has no valid
    /// use, so both cases resolve to <see cref="DefaultSelectFields"/>. A consumer who configures
    /// this property gets exactly the fields they configured, with no defaults mixed in.
    /// </summary>
    public IReadOnlyList<string> SelectFields { get; set; } = [];

    /// <summary>
    /// Whether to expand <c>singleValueExtendedProperties</c> for the MAPI tags configured by
    /// <see cref="MapiInReplyToTag"/> and <see cref="MapiReferencesTag"/>, recovering the
    /// <c>In-Reply-To</c> and <c>References</c> headers. Headers are not returned via
    /// <c>internetMessageHeaders</c> in <c>$select</c> unless the message crossed an internet
    /// boundary, but the MAPI extended properties are populated whenever headers are present, so
    /// this expansion is how those two headers are recovered. Defaults to <c>true</c>: today's
    /// client expands unconditionally and has no toggle.
    /// </summary>
    public bool ExpandMapiHeaders { get; set; } = true;

    /// <summary>
    /// The MAPI extended-property id for the <c>In-Reply-To</c> header.
    /// </summary>
    public string MapiInReplyToTag { get; set; } = "String 0x1042";

    /// <summary>
    /// The MAPI extended-property id for the <c>References</c> header.
    /// </summary>
    public string MapiReferencesTag { get; set; } = "String 0x1039";
}

/// <summary>
/// Configuration for the Graph delta query used to page through a mailbox's messages.
/// </summary>
public sealed class GraphDeltaOptions
{
    // Mutable get/set properties, not init-only: this type is bound from configuration.

    /// <summary>
    /// The mail-folder path segment the delta query scopes to (e.g. <c>inbox</c>). Defaults to
    /// today's fixed scope.
    /// </summary>
    public string FolderScope { get; set; } = "inbox";

    /// <summary>
    /// The Graph <c>$select</c> fields requested on the delta query. When null, the delta query
    /// reuses <see cref="GraphMessageOptions.SelectFields"/>, preserving today's behavior of
    /// using the same select list for both the single-message fetch and the delta query.
    /// </summary>
    public IReadOnlyList<string>? SelectFields { get; set; }
}

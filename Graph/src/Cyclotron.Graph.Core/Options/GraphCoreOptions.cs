using Cyclotron.Graph.Core.Models;

namespace Cyclotron.Graph.Core.Options;

/// <summary>
/// The auth mode a consumer configures for <c>Cyclotron.Graph.Core</c>'s token provider.
/// </summary>
public enum GraphAuthMode
{
    /// <summary>Client credentials flow using a tenant id, client id, and client secret.</summary>
    ClientSecret,

    /// <summary>Azure.Identity's <c>DefaultAzureCredential</c> (managed identity, <c>az login</c>, etc.).</summary>
    DefaultAzureCredential,

    /// <summary>A consumer-registered <see cref="Abstractions.IGraphTokenProvider"/> implementation.</summary>
    Custom,
}

/// <summary>
/// Root configuration for <c>Cyclotron.Graph.Core</c>: authentication, subscriptions, and the
/// HTTP endpoints Core's clients target. Bound from the section named by
/// <see cref="DefaultSectionName"/> unless a consumer's DI wiring specifies a different section.
/// </summary>
public sealed class GraphCoreOptions
{
    /// <summary>The default configuration section name this options type binds from.</summary>
    public const string DefaultSectionName = "Graph";

    // Mutable get/set properties, not init-only: this type is bound from configuration via
    // Microsoft.Extensions.Options, which requires a public parameterless constructor and
    // settable properties. The repo-wide "prefer init-only" convention does not apply here.

    /// <summary>Authentication configuration.</summary>
    public GraphAuthOptions Auth { get; set; } = new();

    /// <summary>Subscription configuration.</summary>
    public GraphSubscriptionOptions Subscription { get; set; } = new();

    /// <summary>The base address of the Microsoft Graph API.</summary>
    public string BaseAddress { get; set; } = "https://graph.microsoft.com/";

    /// <summary>The Graph API version segment used in request routes (e.g. <c>v1.0/subscriptions</c>).</summary>
    public string ApiVersion { get; set; } = "v1.0";

    /// <summary>
    /// The value sent in the <c>Prefer</c> request header on Graph calls. Defaults to today's
    /// value, which includes an Outlook-specific token (<c>outlook.body-content-type</c>) that a
    /// non-mail consumer may clear or replace.
    /// </summary>
    public string PreferHeader { get; set; } = "IdType=\"ImmutableId\", outlook.body-content-type=\"text\"";

    /// <summary>The base address of the Microsoft Entra login endpoint used for token acquisition.</summary>
    public string LoginBaseAddress { get; set; } = "https://login.microsoftonline.com/";
}

/// <summary>
/// Authentication configuration for <c>Cyclotron.Graph.Core</c>'s token provider.
/// </summary>
public sealed class GraphAuthOptions
{
    // Mutable get/set properties, not init-only: this type is bound from configuration.

    /// <summary>Which authentication mode the token provider uses.</summary>
    public GraphAuthMode Mode { get; set; } = GraphAuthMode.ClientSecret;

    /// <summary>The Microsoft Entra tenant id, used by <see cref="GraphAuthMode.ClientSecret"/>.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The app registration's client id, used by <see cref="GraphAuthMode.ClientSecret"/>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The app registration's client secret, used by <see cref="GraphAuthMode.ClientSecret"/>.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The OAuth scope requested when acquiring a token.</summary>
    public string Scope { get; set; } = "https://graph.microsoft.com/.default";

    /// <summary>
    /// Seconds subtracted from the token's reported expiry to build in a clock-skew buffer, so a
    /// cached token is not returned right at the edge of its actual validity window.
    /// </summary>
    public int TokenExpiryBufferSeconds { get; set; } = 90;
}

/// <summary>
/// Subscription configuration for <c>Cyclotron.Graph.Core</c>'s subscription client.
/// </summary>
public sealed class GraphSubscriptionOptions
{
    // Mutable get/set properties, not init-only: this type is bound from configuration.

    /// <summary>The fully-qualified change-notification webhook URL Graph posts to.</summary>
    public string NotificationUrl { get; set; } = string.Empty;

    /// <summary>The fully-qualified lifecycle-notification webhook URL Graph posts to.</summary>
    public string LifecycleNotificationUrl { get; set; } = string.Empty;

    /// <summary>The secret embedded as <c>clientState</c> on created subscriptions and validated on incoming notifications.</summary>
    public string ClientStateSecret { get; set; } = string.Empty;

    /// <summary>
    /// The resource-path template used when subscribing, with the literal token
    /// <c>{resourceId}</c> substituted with the caller-supplied resource id at call time. This is
    /// caller-supplied configuration: Core never assumes the resource is mail, and
    /// <c>Cyclotron.Graph.Mail</c> overrides this default with its own mail-shaped template.
    /// </summary>
    public string ResourceTemplate { get; set; } = "users/{resourceId}/messages";

    /// <summary>The change types new subscriptions are created to cover.</summary>
    public GraphChangeType ChangeTypes { get; set; } = GraphChangeType.Created | GraphChangeType.Updated;

    /// <summary>How long, in minutes, a newly created subscription is valid for. Graph's own maximum is 10080 (7 days).</summary>
    public int LifespanMinutes { get; set; } = 10_000;

    /// <summary>
    /// How far ahead of expiry, in hours, a subscription is considered "expiring soon" and should
    /// be renewed by a periodic true-up.
    /// </summary>
    public int RenewWindowHours { get; set; } = 6;
}

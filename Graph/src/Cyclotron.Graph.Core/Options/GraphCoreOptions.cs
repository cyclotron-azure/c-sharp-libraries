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
/// Root configuration for <c>Cyclotron.Graph.Core</c>: authentication and subscriptions. Bound
/// from the section named by <see cref="DefaultSectionName"/> unless a consumer's DI wiring
/// specifies a different section. The commercial-cloud Graph API and Entra login hosts are fixed
/// in <c>GraphCoreServiceCollectionExtensions</c>, not configurable here — this repo has no
/// sovereign/national-cloud consumer.
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

    /// <summary>The Graph API version segment used in request routes (e.g. <c>v1.0/subscriptions</c>).</summary>
    public string ApiVersion { get; set; } = "v1.0";

    /// <summary>
    /// The value sent in the <c>Prefer</c> request header on Graph calls. Defaults to today's
    /// value, which includes an Outlook-specific token (<c>outlook.body-content-type</c>) that a
    /// non-mail consumer may clear or replace.
    /// </summary>
    public string PreferHeader { get; set; } = "IdType=\"ImmutableId\", outlook.body-content-type=\"text\"";
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

    /// <summary>
    /// The public base URL (scheme + host — e.g. a dev tunnel while developing locally, or the
    /// deployed app's own address) that <see cref="NotificationPath"/> and
    /// <see cref="LifecycleNotificationPath"/> are appended to, forming
    /// <see cref="NotificationUrl"/> and <see cref="LifecycleNotificationUrl"/>. Graph rejects a
    /// non-publicly-routable host (including <c>localhost</c>) when creating a subscription, so
    /// this must be reachable from the internet.
    /// </summary>
    public string NotificationBaseUrl { get; set; } = string.Empty;

    /// <summary>The path appended to <see cref="NotificationBaseUrl"/> to form <see cref="NotificationUrl"/>.</summary>
    public string NotificationPath { get; set; } = string.Empty;

    /// <summary>The path appended to <see cref="NotificationBaseUrl"/> to form <see cref="LifecycleNotificationUrl"/>.</summary>
    public string LifecycleNotificationPath { get; set; } = string.Empty;

    /// <summary>
    /// The fully-qualified change-notification webhook URL Graph posts to — computed from
    /// <see cref="NotificationBaseUrl"/> + <see cref="NotificationPath"/>, so switching to a new
    /// base (a new tunnel, for example) only means updating one setting.
    /// </summary>
    public string NotificationUrl => Combine(NotificationBaseUrl, NotificationPath);

    /// <summary>
    /// The fully-qualified lifecycle-notification webhook URL Graph posts to — computed from
    /// <see cref="NotificationBaseUrl"/> + <see cref="LifecycleNotificationPath"/>.
    /// </summary>
    public string LifecycleNotificationUrl => Combine(NotificationBaseUrl, LifecycleNotificationPath);

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

    private static string Combine(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }
}

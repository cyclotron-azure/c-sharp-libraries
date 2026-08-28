using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Models;
using Cyclotron.Graph.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Client;

/// <summary>
/// Creates, queries, renews, and deletes Microsoft Graph change-notification subscriptions
/// against the <c>/subscriptions</c> endpoint. Resource-agnostic: every member operates on a
/// caller-supplied resource identifier and never assumes the subscribed resource is mail.
/// </summary>
internal sealed class GraphSubscriptionClient : IGraphSubscriptionClient
{
    private readonly HttpClient _client;
    private readonly GraphCoreOptions _options;
    private readonly ILogger<GraphSubscriptionClient> _logger;

    public GraphSubscriptionClient(
        HttpClient client, IOptions<GraphCoreOptions> options, ILogger<GraphSubscriptionClient> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GraphSubscriptionInfo> SubscribeAsync(string resourceId, CancellationToken ct = default)
    {
        var subscriptionOptions = _options.Subscription;
        var expiryUtc = DateTimeOffset.UtcNow.AddMinutes(subscriptionOptions.LifespanMinutes);

        var resource = subscriptionOptions.ResourceTemplate.Replace(
            "{resourceId}", Uri.EscapeDataString(resourceId));

        var payload = new
        {
            changeType = subscriptionOptions.ChangeTypes.ToGraphString(),
            notificationUrl = subscriptionOptions.NotificationUrl,
            lifecycleNotificationUrl = subscriptionOptions.LifecycleNotificationUrl,
            resource,
            expirationDateTime = expiryUtc.ToString("o"),
            clientState = subscriptionOptions.ClientStateSecret
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Creating Graph subscription for {ResourceId}", resourceId);

        var response = await _client.PostAsync($"{_options.ApiVersion}/subscriptions", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken: ct);
            _logger.LogError(
                "Failed to create subscription for {ResourceId}; Graph returned {Status}: {Error}",
                resourceId, (int)response.StatusCode, error);
            response.EnsureSuccessStatusCode();
        }

        var subscription = await response.Content.ReadFromJsonAsync<SubscriptionDto>(cancellationToken: ct);
        if (subscription is null || string.IsNullOrEmpty(subscription.Id))
        {
            throw new InvalidOperationException($"Graph subscription response missing id for {resourceId}");
        }

        // Graph echoes changeType on a successful create. ChangeTypes parses that string into a
        // non-nullable flags enum, so an omitted (or wholly unrecognized) changeType collapses to
        // None -- indistinguishable from a real value. Handing None back would describe the
        // subscription as covering nothing, and a later query filtered by change type would skip
        // the subscription that was just created. A contract violation must surface rather than be
        // substituted with a guess.
        if (subscription.ChangeTypes == GraphChangeType.None)
        {
            throw new InvalidOperationException(
                $"Graph subscription response missing changeType for {resourceId}");
        }

        _logger.LogInformation(
            "Graph subscription created for {ResourceId}: Id={SubscriptionId} Expiry={Expiry:u}",
            resourceId, subscription.Id, subscription.ExpirationDateTime);

        return new GraphSubscriptionInfo(
            subscription.Id, subscription.ParseResourceId(), subscription.ChangeTypes, subscription.ExpirationDateTime);
    }

    public async Task<HashSet<string>> GetActiveSubscribedResourceIdsAsync(
        GraphChangeType changeTypes = GraphChangeType.None, CancellationToken ct = default)
    {
        var response = await _client.GetAsync($"{_options.ApiVersion}/subscriptions", ct);
        response.EnsureSuccessStatusCode();

        var list = await response.Content.ReadFromJsonAsync<SubscriptionListDto>(cancellationToken: ct);

        // A subscription's changeType is itself a comma-separated list of tokens, so we
        // keep any subscription whose covered change types overlap the requested ones. With
        // GraphChangeType.None requested, every subscription is kept (no filtering).
        //
        // Deliberately does NOT apply the None guard the construction sites apply: this projects
        // to resource-id strings rather than to GraphSubscriptionInfo, and it sweeps every
        // subscription in the tenant. One subscription with a degraded changeType is filtered out
        // of the results — visible and self-correcting, since the next reconciliation pass sees it
        // as absent and recreates it. Throwing here would instead fail the entire sweep.
        //
        // The returned segments are deliberately raw and unnormalized — do NOT lowercase, trim,
        // resolve, or canonicalize them here. RecreateAllMissingAsync's transitional address arm
        // (a caller reconciling against both an id and a legacy address) only works if the raw
        // segment survives exactly as parsed; normalizing here would silently make the goal's
        // backfill-storm mitigation inert.
        var resourceIds = list?.Subscriptions?
            .Where(s => changeTypes == GraphChangeType.None || (s.ChangeTypes & changeTypes) != 0)
            .Select(s => s.ParseResourceId())
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        return resourceIds;
    }

    public async Task<GraphSubscriptionInfo?> GetSubscriptionByIdAsync(string subscriptionId, CancellationToken ct = default)
    {
        var response = await _client.GetAsync($"{_options.ApiVersion}/subscriptions/{Uri.EscapeDataString(subscriptionId)}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<SubscriptionDto>(cancellationToken: ct);
        if (dto is null || string.IsNullOrEmpty(dto.Id))
        {
            throw new InvalidOperationException(
                $"Graph subscription response missing id for subscriptionId {subscriptionId}");
        }

        return GraphSubscriptionMapping.MapFrom(dto);
    }

    public Task<GraphSubscriptionInfo?> GetSubscriptionByResourceIdAsync(string resourceId, CancellationToken ct = default) =>
        GetSubscriptionByResourceIdentifiersAsync([resourceId], ct);

    public async Task<GraphSubscriptionInfo?> GetSubscriptionByResourceIdentifiersAsync(
        IReadOnlyCollection<string> resourceIdentifiers, CancellationToken ct = default)
    {
        if (resourceIdentifiers is null || resourceIdentifiers.Count == 0)
        {
            throw new ArgumentException("At least one resource identifier must be supplied.", nameof(resourceIdentifiers));
        }

        // Graph's GET /subscriptions has no server-side filter by resource, so we list the
        // tenant's subscriptions and match client-side.
        var response = await _client.GetAsync($"{_options.ApiVersion}/subscriptions", ct);
        response.EnsureSuccessStatusCode();

        var list = await response.Content.ReadFromJsonAsync<SubscriptionListDto>(cancellationToken: ct);

        // Note: there is no way to query Graph for a subscription by resource identifier, so we
        // have to fetch all active subscriptions and check if any supplied identifier matches.
        var match = list?.Subscriptions?
            .FirstOrDefault(s => resourceIdentifiers.Any(
                id => string.Equals(s.ParseResourceId(), id, StringComparison.OrdinalIgnoreCase)));

        if (match is null || string.IsNullOrEmpty(match.Id))
        {
            return null;
        }

        return GraphSubscriptionMapping.MapFrom(match);
    }

    public async Task<DateTimeOffset> RenewSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        var expiryUtc = DateTimeOffset.UtcNow.AddMinutes(_options.Subscription.LifespanMinutes);
        // graph requires ISO 8601 format with correct microseconds that ToString("o") produces
        var payload = new { expirationDateTime = expiryUtc.ToString("o") };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Renewing Graph subscription {SubscriptionId}", subscriptionId);

        var escapedSubscriptionId = Uri.EscapeDataString(subscriptionId);
        var response = await _client.PatchAsync($"{_options.ApiVersion}/subscriptions/{escapedSubscriptionId}", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken: ct);
            _logger.LogError(
                "Failed to renew subscription {SubscriptionId}; Graph returned {Status}: {Error}",
                subscriptionId, (int)response.StatusCode, error);
            response.EnsureSuccessStatusCode();
        }

        var updated = await response.Content.ReadFromJsonAsync<SubscriptionDto>(cancellationToken: ct);

        // Graph guarantees expirationDateTime on a successful renew. ExpirationDateTime is a
        // non-nullable value type, so an omitted property deserializes to default (0001-01-01) —
        // returning that would hand the caller a just-renewed subscription that looks permanently
        // expired. A contract violation must surface rather than be substituted with a guess.
        if (updated is null || updated.ExpirationDateTime == default)
        {
            throw new InvalidOperationException(
                $"Graph subscription response missing expirationDateTime for subscriptionId {subscriptionId}");
        }

        var newExpiry = updated.ExpirationDateTime;

        _logger.LogInformation(
            "Renewed subscription {SubscriptionId}; new expiry={Expiry:u}", subscriptionId, newExpiry);
        return newExpiry;
    }

    public async Task UnsubscribeAsync(string subscriptionId, CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting Graph subscription {SubscriptionId}", subscriptionId);
        var response = await _client.DeleteAsync(
            $"{_options.ApiVersion}/subscriptions/{Uri.EscapeDataString(subscriptionId)}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Subscription {SubscriptionId} not found on Graph — may already be gone", subscriptionId);
            return;
        }

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Deleted subscription {SubscriptionId}", subscriptionId);
    }
}

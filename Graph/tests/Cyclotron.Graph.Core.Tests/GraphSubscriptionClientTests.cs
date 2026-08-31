using System.Net;
using System.Text.Json;
using Cyclotron.Graph.Core.Client;
using Cyclotron.Graph.Core.Models;
using Cyclotron.Graph.Core.Options;
using Cyclotron.Graph.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace Cyclotron.Graph.Core.Tests;

public class GraphSubscriptionClientTests
{
    private const string BaseAddress = "https://graph.test/";

    private static (GraphSubscriptionClient Client, MockHttpHandlerBuilder Handler) CreateClient(
        Action<GraphCoreOptions>? configure = null)
    {
        var options = new GraphCoreOptions
        {
            Subscription =
            {
                ClientStateSecret = "client-state-secret",
                NotificationBaseUrl = "https://example.test",
                NotificationPath = "/notifications",
                LifecycleNotificationPath = "/lifecycle",
            },
        };
        configure?.Invoke(options);

        var handler = new MockHttpHandlerBuilder();
        var httpClient = handler.CreateHttpClient(BaseAddress);

        var client = new GraphSubscriptionClient(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<GraphSubscriptionClient>.Instance);

        return (client, handler);
    }

    private static string SubscriptionListJson(params string[] subscriptionObjects) =>
        $$"""{ "value": [ {{string.Join(",", subscriptionObjects)}} ] }""";

    private static string SubscriptionJson(string id, string resource, string changeType, string expiry = "2030-01-01T00:00:00Z") =>
        $$"""
        {
          "id": "{{id}}",
          "resource": "{{resource}}",
          "changeType": "{{changeType}}",
          "expirationDateTime": "{{expiry}}"
        }
        """;

    [Fact]
    public async Task SubscribeAsync_ConfiguredChangeTypes_PostsDerivedChangeTypeAndResource()
    {
        var (client, handler) = CreateClient(o =>
            o.Subscription.ChangeTypes = GraphChangeType.Created | GraphChangeType.Deleted);
        handler.RespondWithJson(SubscriptionJson("sub-1", "users/user-1/messages", "created,deleted"));

        await client.SubscribeAsync("user-1", TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(handler.LastRequest.Body!).RootElement;
        Assert.Equal("created,deleted", body.GetProperty("changeType").GetString());
        Assert.NotEqual("created,updated", body.GetProperty("changeType").GetString());
        Assert.Equal("users/user-1/messages", body.GetProperty("resource").GetString());
    }

    [Fact]
    public async Task SubscribeAsync_ResponseWithoutId_ThrowsInvalidOperationException()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""{ "resource": "users/user-1/messages" }""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SubscribeAsync("user-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetActiveSubscribedResourceIdsAsync_NoneFilter_ReturnsEverySubscriptionsResourceSegment()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionListJson(
            SubscriptionJson("sub-1", "users/user-1/messages", "created"),
            SubscriptionJson("sub-2", "users/user-2/messages", "deleted")));

        var actual = await client.GetActiveSubscribedResourceIdsAsync(
            GraphChangeType.None, TestContext.Current.CancellationToken);

        Assert.Equal(["user-1", "user-2"], actual.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetActiveSubscribedResourceIdsAsync_SpecificFlag_ReturnsOnlyOverlappingSubscriptions()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionListJson(
            SubscriptionJson("sub-1", "users/user-1/messages", "created,updated"),
            SubscriptionJson("sub-2", "users/user-2/messages", "deleted")));

        var actual = await client.GetActiveSubscribedResourceIdsAsync(
            GraphChangeType.Deleted, TestContext.Current.CancellationToken);

        Assert.Equal(["user-2"], actual);
    }

    [Fact]
    public async Task GetActiveSubscribedResourceIdsAsync_ReturnedSet_IsCaseInsensitive()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionListJson(
            SubscriptionJson("sub-1", "users/user-1/messages", "created")));

        var actual = await client.GetActiveSubscribedResourceIdsAsync(
            GraphChangeType.None, TestContext.Current.CancellationToken);

        Assert.Contains("USER-1", actual);
    }

    /// <summary>
    /// The resource segment is returned exactly as parsed — not lowercased, trimmed, or otherwise
    /// canonicalized. The goal's backfill-storm mitigation reconciles against both an id and a
    /// legacy address form, and normalizing here would silently make that mitigation inert.
    /// </summary>
    [Fact]
    public async Task GetActiveSubscribedResourceIdsAsync_AddressKeyedSubscription_ReturnsAddressUnnormalized()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionListJson(
            SubscriptionJson("sub-1", "users/User@Contoso.com/messages", "created")));

        var actual = await client.GetActiveSubscribedResourceIdsAsync(
            GraphChangeType.None, TestContext.Current.CancellationToken);

        Assert.Equal(["User@Contoso.com"], actual);
    }

    [Fact]
    public async Task GetSubscriptionByResourceIdAsync_DifferentlyCasedId_ReturnsMatchingSubscription()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionListJson(
            SubscriptionJson("sub-1", "users/user-1/messages", "created")));

        var actual = await client.GetSubscriptionByResourceIdAsync("USER-1", TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.Equal("sub-1", actual.Id);
    }

    [Fact]
    public async Task GetSubscriptionByResourceIdAsync_NoMatchingSubscription_ReturnsNull()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionListJson(
            SubscriptionJson("sub-1", "users/user-1/messages", "created")));

        var actual = await client.GetSubscriptionByResourceIdAsync("user-other", TestContext.Current.CancellationToken);

        Assert.Null(actual);
    }

    /// <summary>
    /// The regression guard for the documented "Known operational consequence": a subscription
    /// created before the caller migrated identifier forms holds an address, so it is found only
    /// when the caller passes both its old and new identifiers.
    /// </summary>
    [Fact]
    public async Task GetSubscriptionByResourceIdentifiersAsync_AddressKeyedSubscription_FoundOnlyWithBothIdentifiers()
    {
        const string subscriptionListJson = """
        { "value": [ { "id": "sub-1", "resource": "users/user@contoso.com/messages", "changeType": "created", "expirationDateTime": "2030-01-01T00:00:00Z" } ] }
        """;
        var (client, handler) = CreateClient();
        handler.RespondWithJson(subscriptionListJson).RespondWithJson(subscriptionListJson);

        var withBoth = await client.GetSubscriptionByResourceIdentifiersAsync(
            ["user-1", "user@contoso.com"], TestContext.Current.CancellationToken);
        var withIdOnly = await client.GetSubscriptionByResourceIdentifiersAsync(
            ["user-1"], TestContext.Current.CancellationToken);

        Assert.NotNull(withBoth);
        Assert.Equal("sub-1", withBoth.Id);
        Assert.Null(withIdOnly);
    }

    [Fact]
    public async Task GetSubscriptionByResourceIdentifiersAsync_EmptyCollection_ThrowsArgumentException()
    {
        var (client, _) = CreateClient();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetSubscriptionByResourceIdentifiersAsync([], TestContext.Current.CancellationToken));

        Assert.Equal("resourceIdentifiers", exception.ParamName);
    }

    [Fact]
    public async Task RenewSubscriptionAsync_ServerSuppliedExpiry_PatchesAndReturnsServerValue()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionJson("sub-1", "users/user-1/messages", "created", "2031-06-01T12:00:00Z"));

        var actual = await client.RenewSubscriptionAsync("sub-1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest.Method);
        Assert.Equal(DateTimeOffset.Parse("2031-06-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture), actual);
    }

    /// <summary>
    /// A JSON <c>null</c> body carries no expiry at all, so there is nothing to return; the client
    /// fails hard rather than substituting a locally computed value.
    /// </summary>
    [Fact]
    public async Task RenewSubscriptionAsync_NullResponseBody_ThrowsInvalidOperationException()
    {
        var (client, handler) = CreateClient(o => o.Subscription.LifespanMinutes = 60);
        handler.RespondWithJson("null");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.RenewSubscriptionAsync("sub-1", TestContext.Current.CancellationToken));

        Assert.Contains("sub-1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// SubscriptionDto.ExpirationDateTime is a non-nullable DateTimeOffset, so a response object
    /// that OMITS expirationDateTime deserializes it to default (0001-01-01). Returning that would
    /// hand the caller a just-renewed subscription that looks permanently expired, so the client
    /// treats the default value as an absent field and throws.
    /// </summary>
    [Fact]
    public async Task RenewSubscriptionAsync_ResponseObjectOmittingExpiry_ThrowsInvalidOperationException()
    {
        var (client, handler) = CreateClient(o => o.Subscription.LifespanMinutes = 60);
        handler.RespondWithJson("""{ "id": "sub-1" }""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.RenewSubscriptionAsync("sub-1", TestContext.Current.CancellationToken));

        Assert.Contains("sub-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsubscribeAsync_NotFound_ReturnsWithoutThrowing()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithStatus(HttpStatusCode.NotFound);

        await client.UnsubscribeAsync("sub-1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Delete, handler.LastRequest.Method);
    }

    [Fact]
    public async Task UnsubscribeAsync_ServerError_ThrowsHttpRequestException()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithStatus(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.UnsubscribeAsync("sub-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSubscriptionByIdAsync_Found_ReturnsMappedSubscriptionInfo()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionJson("sub-1", "users/user-1/messages", "created,updated", "2030-05-04T03:02:01Z"));

        var actual = await client.GetSubscriptionByIdAsync("sub-1", TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.Equal("sub-1", actual.Id);
        Assert.Equal("user-1", actual.ResourceId);
        Assert.Equal(GraphChangeType.Created | GraphChangeType.Updated, actual.ChangeTypes);
        Assert.Equal(
            DateTimeOffset.Parse("2030-05-04T03:02:01Z", System.Globalization.CultureInfo.InvariantCulture),
            actual.ExpiresAt);
    }

    /// <summary>
    /// SubscriptionDto.ChangeTypes parses Graph's comma-separated changeType into a non-nullable
    /// flags enum, so a create response that OMITS changeType collapses to GraphChangeType.None.
    /// Returning that would describe the just-created subscription as covering no change types,
    /// so the client treats None as an absent field and throws.
    /// </summary>
    [Fact]
    public async Task SubscribeAsync_ResponseObjectOmittingChangeType_ThrowsInvalidOperationException()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""{ "id": "sub-1", "resource": "users/user-1/messages", "expirationDateTime": "2030-01-01T00:00:00Z" }""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SubscribeAsync("user-1", TestContext.Current.CancellationToken));

        Assert.Contains("user-1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A changeType carrying only tokens this library does not recognize parses to None just as an
    /// absent field does, and is equally unusable — so it fails the same way.
    /// </summary>
    [Fact]
    public async Task SubscribeAsync_ResponseWithOnlyUnrecognizedChangeTypes_ThrowsInvalidOperationException()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionJson("sub-1", "users/user-1/messages", "renamed,archived"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SubscribeAsync("user-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The same guard applies on the mapping path: a fetched subscription whose changeType is
    /// absent would map to a GraphSubscriptionInfo claiming it covers nothing.
    /// </summary>
    [Fact]
    public async Task GetSubscriptionByIdAsync_ResponseObjectOmittingChangeType_ThrowsInvalidOperationException()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""{ "id": "sub-1", "resource": "users/user-1/messages", "expirationDateTime": "2030-01-01T00:00:00Z" }""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetSubscriptionByIdAsync("sub-1", TestContext.Current.CancellationToken));

        Assert.Contains("sub-1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A missing id and a missing changeType are handled differently on purpose: no id means "no
    /// subscription here" (null), while an id with no changeType means Graph broke its contract
    /// (throw). This pins the null half so the guard above cannot be widened into it.
    /// </summary>
    [Fact]
    public async Task GetSubscriptionByIdAsync_ResponseObjectOmittingId_ThrowsInvalidOperationException()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""{ "resource": "users/user-1/messages", "changeType": "created" }""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetSubscriptionByIdAsync("sub-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The tenant-wide filter path deliberately does NOT throw on a degraded changeType: it
    /// filters that subscription out instead, so one bad subscription cannot fail a whole sweep.
    /// A later reconciliation pass sees it as absent and recreates it.
    /// </summary>
    [Fact]
    public async Task GetActiveSubscribedResourceIdsAsync_SubscriptionWithoutChangeType_IsFilteredOutWithoutThrowing()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson(SubscriptionListJson(
            SubscriptionJson("sub-1", "users/user-1/messages", "created"),
            """{ "id": "sub-2", "resource": "users/user-2/messages", "expirationDateTime": "2030-01-01T00:00:00Z" }"""));

        var actual = await client.GetActiveSubscribedResourceIdsAsync(
            GraphChangeType.Created, TestContext.Current.CancellationToken);

        Assert.Equal(["user-1"], actual);
    }

    [Fact]
    public async Task GetSubscriptionByIdAsync_NotFound_ReturnsNull()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithStatus(HttpStatusCode.NotFound);

        var actual = await client.GetSubscriptionByIdAsync("sub-missing", TestContext.Current.CancellationToken);

        Assert.Null(actual);
    }
}

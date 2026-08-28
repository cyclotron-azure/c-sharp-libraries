using System.Text.Json;
using Cyclotron.Graph.Core.Models;
using Cyclotron.Graph.Core.Notifications;
using Cyclotron.Graph.Core.Options;
using Cyclotron.Graph.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Tests;

/// <summary>
/// Every payload here is a realistic Graph JSON string literal rather than a constructed model
/// object, so GraphNotificationJson.Options — case-insensitive matching plus the camel-case
/// string-enum converter — is genuinely exercised on the way in.
/// </summary>
public class GraphNotificationParserTests
{
    private const string ClientStateSecret = "client-state-secret";

    private static GraphNotificationParser CreateParser(
        GraphChangeType changeTypes = GraphChangeType.Created | GraphChangeType.Updated)
    {
        var options = new GraphCoreOptions();
        options.Subscription.ClientStateSecret = ClientStateSecret;
        options.Subscription.ChangeTypes = changeTypes;

        var wrapped = Microsoft.Extensions.Options.Options.Create(options);

        return new GraphNotificationParser(
            new GraphNotificationValidator(wrapped),
            wrapped,
            NullLogger<GraphNotificationParser>.Instance);
    }

    private static string ChangePayload(
        string changeType = "created",
        string clientState = ClientStateSecret,
        string subscriptionId = "sub-1",
        string resourceId = "msg-1") =>
        $$"""
        {
          "value": [
            {
              "subscriptionId": "{{subscriptionId}}",
              "changeType": "{{changeType}}",
              "clientState": "{{clientState}}",
              "resource": "users/user-1/messages/{{resourceId}}",
              "resourceData": { "id": "{{resourceId}}" }
            }
          ]
        }
        """;

    private static string LifecyclePayload(string lifecycleEvent, string subscriptionId = "sub-1") =>
        $$"""
        {
          "value": [
            {
              "subscriptionId": "{{subscriptionId}}",
              "clientState": "{{ClientStateSecret}}",
              "lifecycleEvent": "{{lifecycleEvent}}"
            }
          ]
        }
        """;

    [Fact]
    public async Task DispatchMessageNotificationsAsync_TwoValidNotifications_DispatchesExactlyTwice()
    {
        var parser = CreateParser();
        var sink = new RecordingMessageSink();
        const string payload = """
        {
          "value": [
            {
              "subscriptionId": "sub-1",
              "changeType": "created",
              "clientState": "client-state-secret",
              "resource": "users/user-1/messages/msg-1",
              "resourceData": { "id": "msg-1" }
            },
            {
              "subscriptionId": "sub-2",
              "changeType": "updated",
              "clientState": "client-state-secret",
              "resource": "users/user-1/messages/msg-2",
              "resourceData": { "id": "msg-2" }
            }
          ]
        }
        """;

        await parser.DispatchMessageNotificationsAsync(payload, sink, TestContext.Current.CancellationToken);

        Assert.Equal(2, sink.Received.Count);
        Assert.Equal(["msg-1", "msg-2"], sink.Received.Select(n => n.ResourceData!.Id));
        Assert.Equal(["sub-1", "sub-2"], sink.Received.Select(n => n.SubscriptionId));
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_InvalidClientState_DispatchesNothing()
    {
        var parser = CreateParser();
        var sink = new RecordingMessageSink();

        await parser.DispatchMessageNotificationsAsync(ChangePayload(clientState: "wrong-secret"), sink, TestContext.Current.CancellationToken);

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_ChangeTypeOutsideConfiguredMask_DispatchesNothing()
    {
        var parser = CreateParser(changeTypes: GraphChangeType.Created);
        var sink = new RecordingMessageSink();

        await parser.DispatchMessageNotificationsAsync(ChangePayload(changeType: "deleted"), sink, TestContext.Current.CancellationToken);

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_MissingResourceData_SkipsWithoutThrowing()
    {
        var parser = CreateParser();
        var sink = new RecordingMessageSink();
        const string payload = """
        {
          "value": [
            {
              "subscriptionId": "sub-1",
              "changeType": "created",
              "clientState": "client-state-secret",
              "resource": "users/user-1/messages/msg-1"
            }
          ]
        }
        """;

        await parser.DispatchMessageNotificationsAsync(payload, sink, TestContext.Current.CancellationToken);

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_EmptyValueArray_DispatchesNothing()
    {
        var parser = CreateParser();
        var sink = new RecordingMessageSink();

        await parser.DispatchMessageNotificationsAsync("""{ "value": [] }""", sink, TestContext.Current.CancellationToken);

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_MalformedJson_ThrowsJsonException()
    {
        var parser = CreateParser();
        var sink = new RecordingMessageSink();

        await Assert.ThrowsAsync<JsonException>(
            () => parser.DispatchMessageNotificationsAsync("{ not json", sink, TestContext.Current.CancellationToken));

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_SinkThrows_PropagatesSinkException()
    {
        var parser = CreateParser();
        var sink = new RecordingMessageSink { ThrowOnDispatch = new InvalidTimeZoneException("sink failed") };

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => parser.DispatchMessageNotificationsAsync(ChangePayload(), sink, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("reauthorizationRequired", LifecycleSinkMethod.ReauthorizationRequired)]
    [InlineData("subscriptionRemoved", LifecycleSinkMethod.SubscriptionRemoved)]
    [InlineData("missed", LifecycleSinkMethod.Missed)]
    public async Task DispatchLifecycleNotificationsAsync_LifecycleEvent_RoutesToMatchingSinkMethodOnce(
        string lifecycleEvent, LifecycleSinkMethod expectedMethod)
    {
        var parser = CreateParser();
        var sink = new RecordingLifecycleSink();

        await parser.DispatchLifecycleNotificationsAsync(LifecyclePayload(lifecycleEvent, "sub-42"), sink, TestContext.Current.CancellationToken);

        var call = Assert.Single(sink.Received);
        Assert.Equal(expectedMethod, call.Method);
        Assert.Equal("sub-42", call.SubscriptionId);
    }

    [Fact]
    public async Task DispatchLifecycleNotificationsAsync_AbsentLifecycleEvent_DispatchesNothingWithoutThrowing()
    {
        var parser = CreateParser();
        var sink = new RecordingLifecycleSink();
        const string payload = """
        {
          "value": [
            {
              "subscriptionId": "sub-1",
              "clientState": "client-state-secret"
            }
          ]
        }
        """;

        await parser.DispatchLifecycleNotificationsAsync(payload, sink, TestContext.Current.CancellationToken);

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task DispatchLifecycleNotificationsAsync_InvalidClientState_DispatchesNothing()
    {
        var parser = CreateParser();
        var sink = new RecordingLifecycleSink();
        const string payload = """
        {
          "value": [
            {
              "subscriptionId": "sub-1",
              "clientState": "wrong-secret",
              "lifecycleEvent": "missed"
            }
          ]
        }
        """;

        await parser.DispatchLifecycleNotificationsAsync(payload, sink, TestContext.Current.CancellationToken);

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task DispatchLifecycleNotificationsAsync_MalformedJson_ThrowsJsonException()
    {
        var parser = CreateParser();
        var sink = new RecordingLifecycleSink();

        await Assert.ThrowsAsync<JsonException>(
            () => parser.DispatchLifecycleNotificationsAsync("{ not json", sink, TestContext.Current.CancellationToken));

        Assert.Empty(sink.Received);
    }
}

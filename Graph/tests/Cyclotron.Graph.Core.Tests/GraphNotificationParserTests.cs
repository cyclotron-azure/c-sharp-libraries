using System.Text.Json;
using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Models;
using Cyclotron.Graph.Core.Notifications;
using Cyclotron.Graph.Core.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Cyclotron.Graph.Core.Tests;

/// <summary>
/// Every payload here is a realistic Graph JSON string literal rather than a constructed model
/// object, so GraphNotificationJson.Options — case-insensitive matching plus the camel-case
/// string-enum converter — is genuinely exercised on the way in.
/// </summary>
public class GraphNotificationParserTests
{
    private const string ClientStateSecret = "client-state-secret";

    /// <summary>Which <see cref="IGraphLifecycleNotificationSink"/> method a routing test expects to fire.</summary>
    public enum LifecycleSinkMethod
    {
        ReauthorizationRequired,
        SubscriptionRemoved,
        Missed,
    }

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

    /// <summary>Builds a message sink mock and a list that records every notification dispatched to it, in order.</summary>
    private static (Mock<IGraphMessageNotificationSink> Mock, List<GraphChangeNotification> Received) CreateMessageSink()
    {
        var mock = new Mock<IGraphMessageNotificationSink>();
        var received = new List<GraphChangeNotification>();
        mock.Setup(s => s.OnResourceChangedAsync(It.IsAny<GraphChangeNotification>(), It.IsAny<CancellationToken>()))
            .Callback<GraphChangeNotification, CancellationToken>((notification, _) => received.Add(notification))
            .Returns(Task.CompletedTask);

        return (mock, received);
    }

    /// <summary>Builds a lifecycle sink mock whose three methods all succeed, for routing assertions via Verify.</summary>
    private static Mock<IGraphLifecycleNotificationSink> CreateLifecycleSink()
    {
        var mock = new Mock<IGraphLifecycleNotificationSink>();
        mock.Setup(s => s.OnReauthorizationRequiredAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(s => s.OnSubscriptionRemovedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(s => s.OnMissedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return mock;
    }

    /// <summary>Verifies none of the three lifecycle sink methods were ever invoked.</summary>
    private static void VerifyNoLifecycleDispatch(Mock<IGraphLifecycleNotificationSink> sink)
    {
        sink.Verify(s => s.OnReauthorizationRequiredAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        sink.Verify(s => s.OnSubscriptionRemovedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        sink.Verify(s => s.OnMissedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_TwoValidNotifications_DispatchesExactlyTwice()
    {
        var parser = CreateParser();
        var (sink, received) = CreateMessageSink();
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

        await parser.DispatchMessageNotificationsAsync(payload, sink.Object, TestContext.Current.CancellationToken);

        Assert.Equal(2, received.Count);
        Assert.Equal(["msg-1", "msg-2"], received.Select(n => n.ResourceData!.Id));
        Assert.Equal(["sub-1", "sub-2"], received.Select(n => n.SubscriptionId));
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_InvalidClientState_DispatchesNothing()
    {
        var parser = CreateParser();
        var (sink, received) = CreateMessageSink();

        await parser.DispatchMessageNotificationsAsync(ChangePayload(clientState: "wrong-secret"), sink.Object, TestContext.Current.CancellationToken);

        Assert.Empty(received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_ChangeTypeOutsideConfiguredMask_DispatchesNothing()
    {
        var parser = CreateParser(changeTypes: GraphChangeType.Created);
        var (sink, received) = CreateMessageSink();

        await parser.DispatchMessageNotificationsAsync(ChangePayload(changeType: "deleted"), sink.Object, TestContext.Current.CancellationToken);

        Assert.Empty(received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_MissingResourceData_SkipsWithoutThrowing()
    {
        var parser = CreateParser();
        var (sink, received) = CreateMessageSink();
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

        await parser.DispatchMessageNotificationsAsync(payload, sink.Object, TestContext.Current.CancellationToken);

        Assert.Empty(received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_EmptyValueArray_DispatchesNothing()
    {
        var parser = CreateParser();
        var (sink, received) = CreateMessageSink();

        await parser.DispatchMessageNotificationsAsync("""{ "value": [] }""", sink.Object, TestContext.Current.CancellationToken);

        Assert.Empty(received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_MalformedJson_ThrowsJsonException()
    {
        var parser = CreateParser();
        var (sink, received) = CreateMessageSink();

        await Assert.ThrowsAsync<JsonException>(
            () => parser.DispatchMessageNotificationsAsync("{ not json", sink.Object, TestContext.Current.CancellationToken));

        Assert.Empty(received);
    }

    [Fact]
    public async Task DispatchMessageNotificationsAsync_SinkThrows_PropagatesSinkException()
    {
        var parser = CreateParser();
        var sink = new Mock<IGraphMessageNotificationSink>();
        sink.Setup(s => s.OnResourceChangedAsync(It.IsAny<GraphChangeNotification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidTimeZoneException("sink failed"));

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => parser.DispatchMessageNotificationsAsync(ChangePayload(), sink.Object, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("reauthorizationRequired", LifecycleSinkMethod.ReauthorizationRequired)]
    [InlineData("subscriptionRemoved", LifecycleSinkMethod.SubscriptionRemoved)]
    [InlineData("missed", LifecycleSinkMethod.Missed)]
    public async Task DispatchLifecycleNotificationsAsync_LifecycleEvent_RoutesToMatchingSinkMethodOnce(
        string lifecycleEvent, LifecycleSinkMethod expectedMethod)
    {
        var parser = CreateParser();
        var sink = CreateLifecycleSink();

        await parser.DispatchLifecycleNotificationsAsync(LifecyclePayload(lifecycleEvent, "sub-42"), sink.Object, TestContext.Current.CancellationToken);

        sink.Verify(
            s => s.OnReauthorizationRequiredAsync("sub-42", It.IsAny<CancellationToken>()),
            expectedMethod == LifecycleSinkMethod.ReauthorizationRequired ? Times.Once() : Times.Never());
        sink.Verify(
            s => s.OnSubscriptionRemovedAsync("sub-42", It.IsAny<CancellationToken>()),
            expectedMethod == LifecycleSinkMethod.SubscriptionRemoved ? Times.Once() : Times.Never());
        sink.Verify(
            s => s.OnMissedAsync("sub-42", It.IsAny<CancellationToken>()),
            expectedMethod == LifecycleSinkMethod.Missed ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task DispatchLifecycleNotificationsAsync_AbsentLifecycleEvent_DispatchesNothingWithoutThrowing()
    {
        var parser = CreateParser();
        var sink = CreateLifecycleSink();
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

        await parser.DispatchLifecycleNotificationsAsync(payload, sink.Object, TestContext.Current.CancellationToken);

        VerifyNoLifecycleDispatch(sink);
    }

    [Fact]
    public async Task DispatchLifecycleNotificationsAsync_InvalidClientState_DispatchesNothing()
    {
        var parser = CreateParser();
        var sink = CreateLifecycleSink();
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

        await parser.DispatchLifecycleNotificationsAsync(payload, sink.Object, TestContext.Current.CancellationToken);

        VerifyNoLifecycleDispatch(sink);
    }

    [Fact]
    public async Task DispatchLifecycleNotificationsAsync_MalformedJson_ThrowsJsonException()
    {
        var parser = CreateParser();
        var sink = CreateLifecycleSink();

        await Assert.ThrowsAsync<JsonException>(
            () => parser.DispatchLifecycleNotificationsAsync("{ not json", sink.Object, TestContext.Current.CancellationToken));

        VerifyNoLifecycleDispatch(sink);
    }
}

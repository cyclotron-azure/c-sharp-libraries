using System.Net;
using System.Text.Json;
using Cyclotron.Graph.Core.Options;
using Cyclotron.Graph.Mail.Client;
using Cyclotron.Graph.Mail.Options;
using Cyclotron.Graph.Mail.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cyclotron.Graph.Mail.Tests;

public class GraphMailClientTests
{
    private const string BaseAddress = "https://graph.test/";
    private const string MailboxId = "mailbox-1";
    private const string MessageId = "message-1";

    /// <summary>
    /// A positional record, mirroring EmailTriage's MessageTriageInfo, so constructor-based
    /// deserialization through PropertyNameCaseInsensitive is genuinely exercised rather than a
    /// settable-property class quietly passing.
    /// </summary>
    public sealed record MessageProjection(string Id, string Subject, bool HasAttachments);

    private static (GraphMailClient Client, StubHttpMessageHandler Handler) CreateClient(
        Action<GraphMailOptions>? configureMail = null)
    {
        var mailOptions = new GraphMailOptions
        {
            Message = { SelectFields = ["subject", "body"] },
        };
        configureMail?.Invoke(mailOptions);

        var coreOptions = new GraphCoreOptions { BaseAddress = BaseAddress };

        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) };

        var client = new GraphMailClient(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(mailOptions),
            Microsoft.Extensions.Options.Options.Create(coreOptions),
            NullLogger<GraphMailClient>.Instance);

        return (client, handler);
    }

    [Fact]
    public async Task ResolveMailboxIdAsync_Found_ReturnsUserId()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""{ "id": "resolved-id" }""");

        var actual = await client.ResolveMailboxIdAsync("user@contoso.com", TestContext.Current.CancellationToken);

        Assert.Equal("resolved-id", actual);
    }

    [Fact]
    public async Task ResolveMailboxIdAsync_NotFound_ReturnsNull()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithStatus(HttpStatusCode.NotFound);

        var actual = await client.ResolveMailboxIdAsync("missing@contoso.com", TestContext.Current.CancellationToken);

        Assert.Null(actual);
    }

    [Fact]
    public async Task ResolveMailboxIdAsync_ServerError_ThrowsHttpRequestException()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithStatus(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ResolveMailboxIdAsync("user@contoso.com", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetMessageAsyncGeneric_CallerSelectFields_BuildsSelectUrlAndDeserializesPositionalRecord()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""{ "id": "message-1", "subject": "Contract Review", "hasAttachments": true }""");

        var actual = await client.GetMessageAsync<MessageProjection>(
            MailboxId, MessageId, ["id", "subject", "hasAttachments"], TestContext.Current.CancellationToken);

        Assert.Contains(
            $"v1.0/users/{MailboxId}/messages/{MessageId}?$select=id,subject,hasAttachments",
            Uri.UnescapeDataString(handler.LastRequest.UriString),
            StringComparison.Ordinal);
        Assert.NotNull(actual);
        Assert.Equal("message-1", actual.Id);
        Assert.Equal("Contract Review", actual.Subject);
        Assert.True(actual.HasAttachments);
    }

    [Fact]
    public async Task GetMessageAsyncGeneric_NotFound_ReturnsNull()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithStatus(HttpStatusCode.NotFound);

        var actual = await client.GetMessageAsync<MessageProjection>(
            MailboxId, MessageId, ["id"], TestContext.Current.CancellationToken);

        Assert.Null(actual);
    }

    [Fact]
    public async Task GetMessageAsyncGeneric_EmptySelectFields_ThrowsArgumentException()
    {
        var (client, _) = CreateClient();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetMessageAsync<MessageProjection>(
                MailboxId, MessageId, [], TestContext.Current.CancellationToken));

        Assert.Equal("selectFields", exception.ParamName);
    }

    [Fact]
    public async Task GetMessageAsync_ExpandMapiHeadersEnabled_IncludesBothConfiguredTagIds()
    {
        var (client, handler) = CreateClient(o =>
        {
            o.Message.ExpandMapiHeaders = true;
            o.Message.MapiInReplyToTag = "String 0x1042";
            o.Message.MapiReferencesTag = "String 0x1039";
        });
        handler.RespondWithJson("""{ "id": "message-1" }""");

        await client.GetMessageAsync(MailboxId, MessageId, TestContext.Current.CancellationToken);

        var uri = Uri.UnescapeDataString(handler.LastRequest.UriString);
        Assert.Contains("$expand=", uri, StringComparison.Ordinal);
        Assert.Contains("String 0x1042", uri, StringComparison.Ordinal);
        Assert.Contains("String 0x1039", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMessageAsync_ExpandMapiHeadersDisabled_OmitsExpandEntirely()
    {
        var (client, handler) = CreateClient(o => o.Message.ExpandMapiHeaders = false);
        handler.RespondWithJson("""{ "id": "message-1" }""");

        await client.GetMessageAsync(MailboxId, MessageId, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("$expand", Uri.UnescapeDataString(handler.LastRequest.UriString), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMessageAsync_MapiExtendedProperties_MapsLineageStampsMailboxAndTrimsBody()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""
        {
          "id": "message-1",
          "subject": "Contract Review",
          "body": { "contentType": "text", "content": "   body text   " },
          "uniqueBody": { "contentType": "text", "content": "  unique text  " },
          "singleValueExtendedProperties": [
            { "id": "String 0x1042", "value": "<parent@contoso.com>" },
            { "id": "String 0x1039", "value": "<a@contoso.com> <b@contoso.com>" }
          ]
        }
        """);

        var actual = await client.GetMessageAsync(MailboxId, MessageId, TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.Equal("<parent@contoso.com>", actual.InReplyTo);
        Assert.Equal(["<b@contoso.com>", "<a@contoso.com>"], actual.References);
        Assert.Equal(MailboxId, actual.MailboxId);
        Assert.Equal("body text", actual.Body);
        Assert.Equal("unique text", actual.UniqueBody);
    }

    [Fact]
    public async Task GetMessageAsync_NotFound_ReturnsNull()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithStatus(HttpStatusCode.NotFound);

        var actual = await client.GetMessageAsync(MailboxId, MessageId, TestContext.Current.CancellationToken);

        Assert.Null(actual);
    }

    [Fact]
    public async Task GetWellKnownFolderIdsAsync_BothFoldersResolve_ReturnsBothIds()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""{ "id": "inbox-id" }""");
        handler.RespondWithJson("""{ "id": "sent-id" }""");

        var actual = await client.GetWellKnownFolderIdsAsync(MailboxId, TestContext.Current.CancellationToken);

        Assert.Equal("inbox-id", actual.InboxFolderId);
        Assert.Equal("sent-id", actual.SentItemsFolderId);
    }

    [Fact]
    public async Task GetWellKnownFolderIdsAsync_SentItemsNotFound_ReturnsInboxIdAndNullSentItems()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""{ "id": "inbox-id" }""");
        handler.RespondWithStatus(HttpStatusCode.NotFound);

        var actual = await client.GetWellKnownFolderIdsAsync(MailboxId, TestContext.Current.CancellationToken);

        Assert.Equal("inbox-id", actual.InboxFolderId);
        Assert.Null(actual.SentItemsFolderId);
    }

    [Fact]
    public async Task UpdateMessageCategoriesAsync_SuppliedCategories_PatchesThoseCategories()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithStatus(HttpStatusCode.OK);

        await client.UpdateMessageCategoriesAsync(
            MailboxId, MessageId, ["AI - LEGAL", "AI - URGENT"], TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest.Method);
        var categories = JsonDocument.Parse(handler.LastRequest.Body!)
            .RootElement.GetProperty("categories")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(["AI - LEGAL", "AI - URGENT"], categories);
    }

    [Fact]
    public async Task UpdateMessageCategoriesAsync_ServerError_ThrowsHttpRequestException()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithStatus(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.UpdateMessageCategoriesAsync(
                MailboxId, MessageId, ["AI - LEGAL"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetMessagesDeltaAsync_MultiplePages_AccumulatesMessagesAndReturnsFinalDeltaLink()
    {
        var (client, handler) = CreateClient();
        handler.RespondWithJson("""
        {
          "value": [ { "id": "m1" }, { "id": "m2" } ],
          "@odata.nextLink": "https://graph.test/v1.0/next-page"
        }
        """);
        handler.RespondWithJson("""
        {
          "value": [ { "id": "m3" } ],
          "@odata.deltaLink": "https://graph.test/v1.0/delta-token"
        }
        """);

        var (messages, deltaLink) = await client.GetMessagesDeltaAsync(
            MailboxId, deltaLink: null, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        Assert.Equal(["m1", "m2", "m3"], messages.Select(m => m.MessageId));
        Assert.Equal("https://graph.test/v1.0/delta-token", deltaLink);
    }

    [Fact]
    public async Task GetMessagesDeltaAsync_GoneResponse_DiscardsAccumulatedMessagesAndRestartsFromFloor()
    {
        var (client, handler) = CreateClient();
        var floor = new DateTimeOffset(2026, 1, 15, 8, 30, 0, TimeSpan.Zero);
        handler.RespondWithJson("""
        {
          "value": [ { "id": "stale" } ],
          "@odata.nextLink": "https://graph.test/v1.0/next-page"
        }
        """);
        handler.RespondWithStatus(HttpStatusCode.Gone);
        handler.RespondWithJson("""
        {
          "value": [ { "id": "fresh" } ],
          "@odata.deltaLink": "https://graph.test/v1.0/delta-token"
        }
        """);

        var (messages, _) = await client.GetMessagesDeltaAsync(
            MailboxId, deltaLink: null, floor, TestContext.Current.CancellationToken);

        Assert.Equal(["fresh"], messages.Select(m => m.MessageId));
        Assert.DoesNotContain("stale", messages.Select(m => m.MessageId));
        var retryUri = Uri.UnescapeDataString(handler.Requests[^1].UriString);
        Assert.Contains("$filter=receivedDateTime+ge+2026-01-15T08:30:00Z", retryUri, StringComparison.Ordinal);
    }

    /// <summary>
    /// Graph quirk: on the delta endpoint the expand clause must be sent WITHOUT the leading '$'
    /// for it to combine with a filter, so the URL carries `&amp;expand=` and never `&amp;$expand=`.
    /// </summary>
    [Fact]
    public async Task GetMessagesDeltaAsync_ConfiguredFolderScope_UsesScopeAndEmitsExpandWithoutDollarSign()
    {
        var (client, handler) = CreateClient(o => o.Delta.FolderScope = "archive");
        handler.RespondWithJson("""{ "value": [], "@odata.deltaLink": "https://graph.test/v1.0/delta-token" }""");

        await client.GetMessagesDeltaAsync(
            MailboxId, deltaLink: null, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        var uri = Uri.UnescapeDataString(handler.LastRequest.UriString);
        Assert.Contains("/mailFolders/archive/messages/delta", uri, StringComparison.Ordinal);
        Assert.Contains("&expand=", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("&$expand=", uri, StringComparison.Ordinal);
    }
}

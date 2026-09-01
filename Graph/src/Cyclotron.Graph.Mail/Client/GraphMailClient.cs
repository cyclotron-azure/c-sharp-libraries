using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cyclotron.Graph.Core.Options;
using Cyclotron.Graph.Mail.Abstractions;
using Cyclotron.Graph.Mail.Models;
using Cyclotron.Graph.Mail.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Mail.Client;

internal sealed class GraphMailClient : IGraphMailClient
{
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _client;
    private readonly GraphMailOptions _mailOptions;
    private readonly GraphCoreOptions _coreOptions;
    private readonly ILogger<GraphMailClient> _logger;

    public GraphMailClient(
        HttpClient client,
        IOptions<GraphMailOptions> mailOptions,
        IOptions<GraphCoreOptions> coreOptions,
        ILogger<GraphMailClient> logger)
    {
        _client = client;
        _mailOptions = mailOptions.Value;
        _coreOptions = coreOptions.Value;
        _logger = logger;
    }

    private string RootRoute => $"{_coreOptions.ApiVersion}/users";

    public async Task<string?> ResolveMailboxIdAsync(string mailboxAddress, CancellationToken ct = default)
    {
        var escaped = Uri.EscapeDataString(mailboxAddress);
        var response = await _client.GetAsync($"{RootRoute}/{escaped}?$select=id", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("User {MailboxAddress} not found when resolving mailbox id", mailboxAddress);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<UserIdResponseDto>(cancellationToken: ct);
        return dto?.Id;
    }

    public async Task<GraphWellKnownFolderIds> GetWellKnownFolderIdsAsync(string mailboxId, CancellationToken ct = default)
    {
        var inbox = await GetFolderIdAsync(mailboxId, "inbox", ct);
        var sentItems = await GetFolderIdAsync(mailboxId, "sentitems", ct);
        return new GraphWellKnownFolderIds(inbox, sentItems);
    }

    public async Task<GraphMailboxMessage?> GetMessageAsync(string mailboxId, string messageId, CancellationToken ct = default)
    {
        var message = _mailOptions.Message;

        var escapedMailboxId = Uri.EscapeDataString(mailboxId);
        var escapedMessageId = Uri.EscapeDataString(messageId);

        var select = string.Join(",", message.SelectFields);
        var url = $"{RootRoute}/{escapedMailboxId}/messages/{escapedMessageId}?$select={select}";
        if (message.ExpandMapiHeaders)
        {
            var expand = BuildMapiExpand(message);
            url += $"&$expand={expand}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        _logger.LogInformation("Fetching message {MessageId} from {MailboxId}", messageId, mailboxId);

        var response = await _client.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Message {MessageId} not found in {MailboxId}", messageId, mailboxId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<MessageDto>(cancellationToken: ct);
        if (dto is null)
        {
            return null;
        }

        var mailboxMessage = GraphMessageMapping.MapToModel(dto, message);
        mailboxMessage.MailboxId = mailboxId;
        return mailboxMessage;
    }

    public async Task<T?> GetMessageAsync<T>(string mailboxId, string messageId, IReadOnlyList<string> selectFields, CancellationToken ct = default)
        where T : class
    {
        if (selectFields is null or { Count: 0 })
        {
            throw new ArgumentException("selectFields must not be null or empty.", nameof(selectFields));
        }

        var escapedMailboxId = Uri.EscapeDataString(mailboxId);
        var escapedMessageId = Uri.EscapeDataString(messageId);
        var select = string.Join(",", selectFields);
        var url = $"{RootRoute}/{escapedMailboxId}/messages/{escapedMessageId}?$select={select}";

        var response = await _client.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Message {MessageId} not found in {MailboxId}", messageId, mailboxId);
            return default;
        }

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, CaseInsensitiveJsonOptions, ct);
    }

    public async Task UpdateMessageCategoriesAsync(string mailboxId, string messageId, IEnumerable<string> categories, CancellationToken ct = default)
    {
        var escapedMailboxId = Uri.EscapeDataString(mailboxId);
        var escapedMessageId = Uri.EscapeDataString(messageId);
        var url = $"{RootRoute}/{escapedMailboxId}/messages/{escapedMessageId}";

        var body = JsonSerializer.Serialize(new { categories });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };

        _logger.LogInformation("Updating categories for message {MessageId} in {MailboxId}: {Categories}", messageId, mailboxId, categories);

        var response = await _client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<(IReadOnlyList<GraphMailboxMessage> Messages, string DeltaLink)> GetMessagesDeltaAsync(
        string mailboxId, string? deltaLink, DateTimeOffset floor, CancellationToken ct = default)
    {
        var delta = _mailOptions.Delta;
        var message = _mailOptions.Message;

        var escapedMailboxId = Uri.EscapeDataString(mailboxId);

        // floor is the caller-supplied backfill floor. Applied on the initial call and on
        // any 410 restart so we never scan mailbox history predating that floor.

        // Graph quirk: only way to use singleValueExtendedProperties+filter on delta endpoint
        // is by removing $ on `expand` and `filter`
        var expand = $"expand={BuildMapiExpand(message).Replace("$", "")}";
        var select = string.Join(",", delta.SelectFields ?? message.SelectFields);
        var floorIso = floor.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var floorEscaped = Uri.EscapeDataString(floorIso);
        var floorUrl = $"{RootRoute}/{escapedMailboxId}/mailFolders/{delta.FolderScope}/messages/delta" +
                       $"?$filter=receivedDateTime+ge+{floorEscaped}&$select={select}&{expand}";

        var url = string.IsNullOrEmpty(deltaLink)
            ? floorUrl
            : deltaLink; // delta link is already an absolute URL

        var messages = new List<GraphMailboxMessage>();
        string? newDeltaLink = null;

        while (url is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Gone)
            {
                // The stored delta token has expired (resyncRequired / syncStateNotFound).
                // Restart from the caller-supplied floor — not from the beginning of mailbox
                // history — so we only replay messages received after that floor.
                _logger.LogWarning("Delta token expired for {MailboxId} (410 Gone) — restarting from floor",
                    mailboxId);
                messages.Clear();
                newDeltaLink = null;
                url = floorUrl;
                continue;
            }

            response.EnsureSuccessStatusCode();

            var page = await response.Content.ReadFromJsonAsync<DeltaResponseDto>(cancellationToken: ct);
            if (page?.Messages is not null)
            {
                foreach (var pageMessage in page.Messages)
                {
                    messages.Add(GraphMessageMapping.MapToModel(pageMessage, message));
                }
            }

            // DeltaLink only appears on the last page, otherwise NextLink appears
            newDeltaLink = page?.DeltaLink;
            url = page?.NextLink;
        }

        _logger.LogInformation(
            "Delta query for {MailboxId}: fetched {Count} messages; deltaLink updated={HasDeltaLink}",
            mailboxId, messages.Count, newDeltaLink is not null);

        return (messages, newDeltaLink!);
    }

    private async Task<string?> GetFolderIdAsync(string mailboxId, string wellKnownName, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(mailboxId);
        var response = await _client.GetAsync($"{RootRoute}/{escaped}/mailFolders/{wellKnownName}?$select=id", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Well-known folder {Folder} not found for {MailboxId}", wellKnownName, mailboxId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<MailFolderIdResponseDto>(cancellationToken: ct);
        return dto?.Id;
    }

    private static string BuildMapiExpand(GraphMessageOptions message) =>
        $"singleValueExtendedProperties($filter=id eq '{message.MapiInReplyToTag}' or id eq '{message.MapiReferencesTag}')";
}

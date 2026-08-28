using System.Text.Json;
using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Models;
using Cyclotron.Graph.Mail.Abstractions;
using Cyclotron.Graph.Mail.Extensions;
using Cyclotron.Graph.Sample;

var builder = WebApplication.CreateBuilder(args);

// One call wires both packages: Cyclotron.Graph.Mail brings Cyclotron.Graph.Core with it.
builder.Services.AddCyclotronGraphMail(builder.Configuration);

// The library registers no sink — the application owns what happens to a notification.
builder.Services.AddSingleton<LoggingNotificationSinks>();
builder.Services.AddSingleton<IGraphMessageNotificationSink>(sp => sp.GetRequiredService<LoggingNotificationSinks>());
builder.Services.AddSingleton<IGraphLifecycleNotificationSink>(sp => sp.GetRequiredService<LoggingNotificationSinks>());

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Every endpoint below is a thin pass-through that returns the library's result as JSON and
// handles nothing: a library exception surfacing as a 500 is exactly what a harness should show.

// IGraphMailClient.ResolveMailboxIdAsync
app.MapGet("/graph/mailboxes/{address}/id", async (
    string address, IGraphMailClient mail, CancellationToken ct) =>
    Results.Ok(new { mailboxId = await mail.ResolveMailboxIdAsync(address, ct) }));

// IGraphMailClient.GetWellKnownFolderIdsAsync
app.MapGet("/graph/mailboxes/{mailboxId}/folders", async (
    string mailboxId, IGraphMailClient mail, CancellationToken ct) =>
    Results.Ok(await mail.GetWellKnownFolderIdsAsync(mailboxId, ct)));

// IGraphMailClient.GetMessageAsync (full, 3-arg)
app.MapGet("/graph/mailboxes/{mailboxId}/messages/{messageId}", async (
    string mailboxId, string messageId, IGraphMailClient mail, CancellationToken ct) =>
    Results.Ok(await mail.GetMessageAsync(mailboxId, messageId, ct)));

// IGraphMailClient.GetMessageAsync<T> (generic, with selectFields) — JsonElement so any field set works.
app.MapGet("/graph/mailboxes/{mailboxId}/messages/{messageId}/select", async (
    string mailboxId, string messageId, string fields, IGraphMailClient mail, CancellationToken ct) =>
    Results.Ok(await mail.GetMessageAsync<JsonElement>(
        mailboxId, messageId, fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ct)));

// IGraphMailClient.UpdateMessageCategoriesAsync
app.MapPatch("/graph/mailboxes/{mailboxId}/messages/{messageId}/categories", async (
    string mailboxId, string messageId, string[] categories, IGraphMailClient mail, CancellationToken ct) =>
{
    await mail.UpdateMessageCategoriesAsync(mailboxId, messageId, categories, ct);
    return Results.NoContent();
});

// IGraphMailClient.GetMessagesDeltaAsync
app.MapGet("/graph/mailboxes/{mailboxId}/delta", async (
    string mailboxId, string? deltaLink, DateTimeOffset? floor, IGraphMailClient mail, CancellationToken ct) =>
{
    var (messages, newDeltaLink) = await mail.GetMessagesDeltaAsync(
        mailboxId, deltaLink, floor ?? DateTimeOffset.UtcNow.AddDays(-7), ct);
    return Results.Ok(new { count = messages.Count, deltaLink = newDeltaLink, messages });
});

// IGraphSubscriptionClient.SubscribeAsync
app.MapPost("/graph/subscriptions/{resourceId}", async (
    string resourceId, IGraphSubscriptionClient subscriptions, CancellationToken ct) =>
    Results.Ok(await subscriptions.SubscribeAsync(resourceId, ct)));

// IGraphSubscriptionClient.GetActiveSubscribedResourceIdsAsync
app.MapGet("/graph/subscriptions/active", async (
    GraphChangeType? changeTypes, IGraphSubscriptionClient subscriptions, CancellationToken ct) =>
    Results.Ok(await subscriptions.GetActiveSubscribedResourceIdsAsync(changeTypes ?? GraphChangeType.None, ct)));

// IGraphSubscriptionClient.GetSubscriptionByIdAsync
app.MapGet("/graph/subscriptions/by-id/{subscriptionId}", async (
    string subscriptionId, IGraphSubscriptionClient subscriptions, CancellationToken ct) =>
    Results.Ok(await subscriptions.GetSubscriptionByIdAsync(subscriptionId, ct)));

// IGraphSubscriptionClient.GetSubscriptionByResourceIdAsync
app.MapGet("/graph/subscriptions/by-resource/{resourceId}", async (
    string resourceId, IGraphSubscriptionClient subscriptions, CancellationToken ct) =>
    Results.Ok(await subscriptions.GetSubscriptionByResourceIdAsync(resourceId, ct)));

// IGraphSubscriptionClient.GetSubscriptionByResourceIdentifiersAsync — the transitional overload.
app.MapGet("/graph/subscriptions/by-identifiers", async (
    string identifiers, IGraphSubscriptionClient subscriptions, CancellationToken ct) =>
    Results.Ok(await subscriptions.GetSubscriptionByResourceIdentifiersAsync(
        identifiers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ct)));

// IGraphSubscriptionClient.RenewSubscriptionAsync
app.MapPatch("/graph/subscriptions/{subscriptionId}", async (
    string subscriptionId, IGraphSubscriptionClient subscriptions, CancellationToken ct) =>
    Results.Ok(new { expiresAt = await subscriptions.RenewSubscriptionAsync(subscriptionId, ct) }));

// IGraphSubscriptionClient.UnsubscribeAsync
app.MapDelete("/graph/subscriptions/{subscriptionId}", async (
    string subscriptionId, IGraphSubscriptionClient subscriptions, CancellationToken ct) =>
{
    await subscriptions.UnsubscribeAsync(subscriptionId, ct);
    return Results.NoContent();
});

// IGraphNotificationParser.DispatchMessageNotificationsAsync — needs no Graph subscription.
app.MapPost("/notifications/messages", async (
    HttpRequest request,
    IGraphNotificationParser parser,
    IGraphMessageNotificationSink sink,
    CancellationToken ct) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(ct);

    await parser.DispatchMessageNotificationsAsync(body, sink, ct);
    return Results.Accepted();
});

// IGraphNotificationParser.DispatchLifecycleNotificationsAsync — needs no Graph subscription.
app.MapPost("/notifications/lifecycle", async (
    HttpRequest request,
    IGraphNotificationParser parser,
    IGraphLifecycleNotificationSink sink,
    CancellationToken ct) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(ct);

    await parser.DispatchLifecycleNotificationsAsync(body, sink, ct);
    return Results.Accepted();
});

app.Run();

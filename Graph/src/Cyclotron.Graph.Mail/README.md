# Cyclotron.Graph.Mail

Microsoft Graph mail client for the `Cyclotron.Graph.*` family — messages, well-known folders, and
delta queries. Depends on `Cyclotron.Graph.Core` for authentication, HTTP configuration,
subscriptions, and change-notification handling; install this package directly and Core arrives
transitively.

## What Mail adds on top of Core

Core knows nothing about mail. Mail adds:

- `IGraphMailClient` — fetch messages, resolve well-known folder ids, update categories, and page
  through the Graph delta query for a mailbox.
- `GraphMailOptions` — the `$select` field list, MAPI extended-property tags for recovering
  `In-Reply-To`/`References` headers, and the delta-query folder scope.

Mail does **not** add its own subscription client — subscribe, renew, and unsubscribe are
resource-agnostic and stay on Core's `IGraphSubscriptionClient` (see below).

## Registering Mail

`AddCyclotronGraphMail` calls `AddCyclotronGraphCore` internally, so **one call** wires up
everything a mail consumer needs — Core's authentication/HTTP/subscription/notification services
plus `IGraphMailClient` — and forgetting to also register Core is not possible. Both overloads
route through one shared private method so the two paths cannot drift.

### From configuration

```csharp
services.AddCyclotronGraphMail(configuration);

// Or, if your settings live under different configuration keys:
services.AddCyclotronGraphMail(configuration, coreSectionName: "MyApp:Graph", mailSectionName: "MyApp:Graph:Mail");
```

### In code

```csharp
services.AddCyclotronGraphMail(
    configureCore: options =>
    {
        options.Auth.Mode = GraphAuthMode.ClientSecret;
        options.Auth.TenantId = "...";
        options.Auth.ClientId = "...";
        options.Auth.ClientSecret = "...";
        options.Subscription.NotificationBaseUrl = "https://myapp.example.com";
        options.Subscription.NotificationPath = "/webhooks/graph";
        options.Subscription.LifecycleNotificationPath = "/webhooks/graph/lifecycle";
        options.Subscription.ClientStateSecret = "...";
    },
    configureMail: options =>
    {
        options.Delta.FolderScope = "inbox";
    });
```

Both `GraphCoreOptions` and `GraphMailOptions` are bound with `.ValidateOnStart()`, so a
misconfigured value fails at application startup rather than on the first Graph call.

## Example configuration

A consumer writes the Core (`Graph`) and Mail (`Graph:Mail`) sections together:

```json
{
  "Graph": {
    "Auth": {
      "Mode": "ClientSecret",
      "TenantId": "",
      "ClientId": "",
      "ClientSecret": "",
      "Scope": "https://graph.microsoft.com/.default",
      "TokenExpiryBufferSeconds": 90
    },
    "Subscription": {
      "NotificationBaseUrl": "",
      "NotificationPath": "",
      "LifecycleNotificationPath": "",
      "ClientStateSecret": "",
      "ResourceTemplate": "users/{resourceId}/messages",
      "ChangeTypes": "Created, Updated",
      "LifespanMinutes": 10000,
      "RenewWindowHours": 6
    },
    "ApiVersion": "v1.0",
    "PreferHeader": "IdType=\"ImmutableId\", outlook.body-content-type=\"text\"",
    "Mail": {
      "Message": {
        "SelectFields": [
          "internetMessageId",
          "subject",
          "receivedDateTime",
          "body",
          "hasAttachments",
          "from",
          "toRecipients",
          "uniqueBody",
          "categories"
        ],
        "ExpandMapiHeaders": true,
        "MapiInReplyToTag": "String 0x1042",
        "MapiReferencesTag": "String 0x1039"
      },
      "Delta": {
        "FolderScope": "inbox",
        "SelectFields": null
      }
    }
  }
}
```

`Message.SelectFields` is shown in full above so the example is copy-pasteable, but the nine field
names are the library's own defaults: if you **omit** the `Graph:Mail:Message:SelectFields` key
entirely, you get exactly those same nine fields, in that same order. Supplying an **empty array**
(`[]`) yields the nine defaults too — the registration cannot tell an unconfigured list from a
deliberately emptied one, and a mail client with no `$select` fields has no valid use, so both mean
"apply the defaults". Configure the key with your own fields and you get exactly those, with none of
the defaults mixed in.

`Delta.SelectFields` is shown as `null` above deliberately — when null, the delta query reuses
`Message.SelectFields`. Setting it to an explicit empty array is a validation failure, not a way to
request "no fields" (see **Validation** below).

## `IGraphMailClient` surface

| Member | Purpose |
|---|---|
| `ResolveMailboxIdAsync(mailboxAddress, ct)` | Resolves the Graph user `id` for a mailbox email address. The only member that takes an address rather than a `mailboxId`. |
| `GetWellKnownFolderIdsAsync(mailboxId, ct)` | Resolves the Graph folder ids of the mailbox's Inbox and Sent Items folders. |
| `GetMessageAsync(mailboxId, messageId, ct)` | Fetches a single message using the configured `Message.SelectFields` (and MAPI expansion, if enabled). |
| `GetMessageAsync<T>(mailboxId, messageId, selectFields, ct)` | Fetches a single message with a caller-supplied `$select` list, deserialized directly into `T`. |
| `UpdateMessageCategoriesAsync(mailboxId, messageId, categories, ct)` | Replaces the category tags on a message. |
| `GetMessagesDeltaAsync(mailboxId, deltaLink, floor, ct)` | Pages through the Graph delta query for the mailbox, returning new/changed messages and an updated delta link. |

## Subscriptions: use Core's `IGraphSubscriptionClient`

`IGraphMailClient` intentionally has no `SubscribeAsync` or similar member. Subscribing to,
renewing, and unsubscribing from a mailbox's change notifications is resource-agnostic plumbing
that lives on `Cyclotron.Graph.Core`'s `IGraphSubscriptionClient` — registered automatically by
`AddCyclotronGraphMail` since it calls `AddCyclotronGraphCore` internally. A mail consumer resolves
both interfaces to get the full set of mail capabilities.

`GraphCoreOptions.Subscription.ResourceTemplate`'s default, `users/{resourceId}/messages`, is
already exactly the resource path a mail consumer needs, so `AddCyclotronGraphMail` does not
override it — the default is simply inherited from Core. A consumer subscribing to a narrower
resource (for example, Sent Items only) overrides `ResourceTemplate` in their own configuration.

### Why `created,updated` and why the whole mailbox messages collection

These two subscription-shape decisions are mail-specific operational reasoning, relocated here
from Core during the extraction:

```
// created + updated: a sent email is first created as a draft, then updated/finalized on send
// (sending changes its id and moves it to Sent Items). The handler filters drafts + folders.

// Subscribe at the mailbox messages collection (all folders) so Sent Items is covered too;
// the handler restricts processing to Inbox + Sent Items via parentFolderId.
```

## Validation

`GraphMailOptionsValidator` returns every failing rule in one consolidated list:

- `Message.SelectFields` must contain at least one field.
- When `Message.ExpandMapiHeaders` is `true`, both `Message.MapiInReplyToTag` and
  `Message.MapiReferencesTag` must be non-empty.
- `Delta.FolderScope` must be configured.
- `Delta.SelectFields`, **when non-null**, must be non-empty. A `null` value is valid and means
  "reuse `Message.SelectFields`"; an explicitly empty list (`[]`) is a misconfiguration — it would
  produce an empty `$select` clause — and fails validation.

Mail's validator does not re-validate any Core-owned option (authentication, subscription, or
endpoint settings) — Core's own validator owns those.

## The mailboxId-not-address rule

Every `IGraphMailClient` member except `ResolveMailboxIdAsync` takes a `mailboxId`, not a mailbox
email address. `mailboxId` is the Graph user `id` returned by `ResolveMailboxIdAsync` — a mailbox
may have alias addresses, so an address is not a stable key to cache or compare against, while the
`id` is. Always resolve the id once via `ResolveMailboxIdAsync` and pass that id to every other
call.

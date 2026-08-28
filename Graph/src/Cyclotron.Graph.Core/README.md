# Cyclotron.Graph.Core

Shared Microsoft Graph plumbing for the `Cyclotron.Graph.*` family — authentication, HTTP client
configuration, subscriptions, and change-notification handling. Most consumers should install a
workload package instead of this one directly: today that's `Cyclotron.Graph.Mail`, which depends
on Core and wires it up for you via `AddCyclotronGraphMail`. Install `Cyclotron.Graph.Core`
directly only when you are building against Graph subscriptions and change notifications with **no
workload package** — for example, a future `Cyclotron.Graph.Teams` consumer that only needs Core's
subscription/notification plumbing. That is a genuinely supported, first-class scenario (this
package is fully installable on its own, matching the `Azure.Core` convention), not a workaround.

## What Core provides

- Token acquisition and caching (`IGraphTokenProvider`), selectable by auth mode.
- The `GraphAuthHandler` that stamps outgoing Graph requests with a bearer token.
- Two named `HttpClient`s: one for the Microsoft Entra token endpoint, one for the Graph API
  itself.
- `IGraphSubscriptionClient` — create, query, renew, and delete Graph change-notification
  subscriptions, for any resource type.
- `IGraphNotificationParser` / `IGraphNotificationValidator` — parse, validate, and dispatch
  incoming change- and lifecycle-notification webhook payloads to sinks you implement.

Core does **not** know about mail. `Message.*`/`Delta.*`-style options and an `IGraphMailClient`
belong to `Cyclotron.Graph.Mail`.

## Registering Core

Both overloads route through the same internal registration logic, so they cannot drift from each
other.

### From configuration

```csharp
services.AddCyclotronGraphCore(configuration);

// Or, if your settings live under a different configuration key:
services.AddCyclotronGraphCore(configuration, sectionName: "MyApp:Graph");
```

### In code

```csharp
services.AddCyclotronGraphCore(options =>
{
    options.Auth.Mode = GraphAuthMode.ClientSecret;
    options.Auth.TenantId = "...";
    options.Auth.ClientId = "...";
    options.Auth.ClientSecret = "...";
    options.Subscription.NotificationUrl = "https://myapp.example.com/webhooks/graph";
    options.Subscription.LifecycleNotificationUrl = "https://myapp.example.com/webhooks/graph/lifecycle";
    options.Subscription.ClientStateSecret = "...";
});
```

Options are bound with `AddOptions<GraphCoreOptions>()` and `.ValidateOnStart()`, so a
misconfigured value fails at application startup rather than on the first Graph call.

## Example configuration

Every `GraphCoreOptions` field, shown with its default value:

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
      "NotificationUrl": "",
      "LifecycleNotificationUrl": "",
      "ClientStateSecret": "",
      "ResourceTemplate": "users/{resourceId}/messages",
      "ChangeTypes": "Created, Updated",
      "LifespanMinutes": 10000,
      "RenewWindowHours": 6
    },
    "BaseAddress": "https://graph.microsoft.com/",
    "ApiVersion": "v1.0",
    "PreferHeader": "IdType=\"ImmutableId\", outlook.body-content-type=\"text\"",
    "LoginBaseAddress": "https://login.microsoftonline.com/"
  }
}
```

`Auth.TenantId`, `Auth.ClientId`, and `Auth.ClientSecret` are required only when `Auth.Mode` is
`ClientSecret`; under `DefaultAzureCredential` or `Custom` they are unused and unvalidated.

`Subscription.ResourceTemplate`'s default (`users/{resourceId}/messages`) is a mail-shaped
convenience default inherited from Core's original extraction context — it is **not** a
Core-level assumption that the subscribed resource is mail. A non-mail consumer (a future
`Cyclotron.Graph.Teams`, for example) overrides it with its own resource path containing the
literal `{resourceId}` token. `Cyclotron.Graph.Mail`'s own `AddCyclotronGraphMail` sets this
appropriately for mail consumers automatically.

## Auth modes

| `Auth.Mode` | Registers | Notes |
|---|---|---|
| `ClientSecret` | `IGraphTokenProvider` → `ClientSecretTokenProvider` | OAuth2 client-credentials grant. |
| `DefaultAzureCredential` | `IGraphTokenProvider` → `DefaultAzureCredentialTokenProvider` | Managed identity, `az login`, etc., via `Azure.Identity`. |
| `Custom` | Nothing | You register your own `IGraphTokenProvider` (singleton — see below), before or after calling `AddCyclotronGraphCore`. |

The built-in providers are registered with `TryAddSingleton`, so a consumer registration made
**before** calling `AddCyclotronGraphCore` always wins over Core's own choice.

## Lifetime invariants

These two lifetimes are load-bearing for correctness, not arbitrary defaults:

- **The token provider is always a singleton.** Its cached access token must survive
  `IHttpClientFactory`'s handler-chain rotation (roughly every two minutes). If it were transient
  or scoped, the token endpoint would be hit every couple of minutes instead of once per actual
  token expiry.
- **`GraphAuthHandler` is always transient**, and captures only singleton dependencies. A
  transient `DelegatingHandler` is recreated on every handler-chain rotation; if it captured a
  *scoped* service, that service would be disposed out from under a pooled handler once its
  originating scope ends, surfacing as `ObjectDisposedException` under load.

Do not "simplify" either of these away.

## Sinks you implement

Core registers **no** sink implementation — that is always the consuming application's
responsibility, at whatever lifetime its own dependencies require (a sink backed by repositories,
for example, will typically be scoped):

- `IGraphMessageNotificationSink` — receives change notifications (Graph's term for its
  non-lifecycle notification stream; this fires for any subscribed resource type, not only mail).
- `IGraphLifecycleNotificationSink` — receives subscription lifecycle events (reauthorization
  required, subscription removed, notifications possibly missed).

Register your implementations at whatever lifetime suits your dependencies, e.g.:

```csharp
services.AddScoped<IGraphMessageNotificationSink, MyNotificationSink>();
services.AddScoped<IGraphLifecycleNotificationSink, MyLifecycleSink>();
```

## Building a mail consumer instead

If you need to read, filter, or react to Outlook mail, don't call `AddCyclotronGraphCore`
directly — install `Cyclotron.Graph.Mail` and call `AddCyclotronGraphMail` instead. It depends on
this package, wires up Core for you with mail-appropriate defaults, and additionally registers
`IGraphMailClient`.

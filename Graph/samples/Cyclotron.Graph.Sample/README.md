# Cyclotron.Graph.Sample

A runnable minimal-API harness for `Cyclotron.Graph.Mail` and `Cyclotron.Graph.Core`. It references
**only** `Cyclotron.Graph.Mail` — Core arrives transitively — and wires both packages with a single
`builder.Services.AddCyclotronGraphMail(builder.Configuration)` call. There is no database, queue,
or telemetry service: the point is to show the packages standing alone.

Every endpoint is a thin pass-through that returns the library's result as JSON and catches
nothing, so a library exception surfaces as a 500 with a ProblemDetails body rather than being
swallowed.

## The three values you must supply

`appsettings.json` ships with **empty secrets** — no credential is committed to this repository.
Supply these three yourself:

| Setting | What it is |
|---|---|
| `Graph:Auth:TenantId` | Your Microsoft Entra tenant id |
| `Graph:Auth:ClientId` | The app registration's client id |
| `Graph:Auth:ClientSecret` | The app registration's client secret |

Plus `Graph:Subscription:ClientStateSecret` — any throwaway string for local runs, but it must
match the `@clientState` variable in `requests/notifications.http` or every notification is
rejected as invalid.

### Supply them via user-secrets, not the committed file

Do not edit `appsettings.json` — it is version-controlled, and a secret typed into it is a secret
committed. Use user-secrets instead (the project already declares a `UserSecretsId`):

```bash
cd Graph/samples/Cyclotron.Graph.Sample
dotnet user-secrets set "Graph:Auth:TenantId" "<tenant-id>"
dotnet user-secrets set "Graph:Auth:ClientId" "<client-id>"
dotnet user-secrets set "Graph:Auth:ClientSecret" "<client-secret>"
dotnet user-secrets set "Graph:Subscription:ClientStateSecret" "<any-throwaway-string>"
```

### Or skip the secret entirely

Switch `Graph:Auth:Mode` to `DefaultAzureCredential` and sign in with the Azure CLI. Then
`TenantId`, `ClientId`, and `ClientSecret` are not required at all — the library validates them
only under `Mode = ClientSecret`:

```bash
az login
dotnet user-secrets set "Graph:Auth:Mode" "DefaultAzureCredential"
```

## Running

```bash
dotnet run --project Graph/samples/Cyclotron.Graph.Sample
```

The HTTP port is pinned to **5280** in `Properties/launchSettings.json`, which is the same port
hard-coded as `@baseUrl` in all three `.http` files. Startup fails fast with an
`OptionsValidationException` listing every misconfigured option at once — that is `ValidateOnStart`
working as intended, not a crash.

## Driving the `.http` files

Open any file under `requests/` in VS Code (REST Client) or Visual Studio and send requests
individually. Each request carries a comment naming the interface member it exercises and the
outcome you should expect, so the files double as manual test documentation.

Fill in the `@` variables at the top of each file first. `@mailboxId` comes from the very first
request in `mail.http` (`ResolveMailboxIdAsync`), which turns an email address into the Graph user
`id` that every other member takes.

| File | Covers |
|---|---|
| `requests/mail.http` | All 6 `IGraphMailClient` members |
| `requests/subscriptions.http` | All 7 `IGraphSubscriptionClient` members |
| `requests/notifications.http` | Both `IGraphNotificationParser` members |

### What needs live Graph, and what does not

- **`/graph/*` endpoints call live Microsoft Graph.** They need real credentials and a mailbox you
  have permission to read. Creating a subscription additionally requires Graph to be able to reach
  `Graph:Subscription:NotificationUrl`, which from a laptop means a public tunnel.
- **The notification endpoints need no Graph subscription and no public tunnel.** `POST
  /notifications/messages` and `POST /notifications/lifecycle` read the raw request body and hand
  it straight to `IGraphNotificationParser`, so `requests/notifications.http` drives the entire
  validation, filtering, and dispatch path completely offline — including the wrong-`clientState`
  skip and the malformed-JSON throw.

Notification outcomes are observed in the **application log**, not the response body: a skipped
notification is a success (202 Accepted), because the library returns normally when a payload is
legitimately filtered out rather than failing in a way that would make a queue trigger retry it.

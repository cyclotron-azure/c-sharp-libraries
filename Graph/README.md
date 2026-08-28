# Cyclotron.Graph

Microsoft Graph integration libraries, split into two packages so that generic Graph mechanisms
(auth, subscriptions, change notifications) can be reused by resource-specific consumers without
pulling in mail-specific surface.

## Packages

| Package | Owns | Depends on |
|---|---|---|
| `Cyclotron.Graph.Core` | Authentication (client-secret and managed-identity token providers), the HTTP/auth handler stack, subscription CRUD, and change-/lifecycle-notification parsing, validation, and dispatch. Resource-agnostic — it knows nothing about mail, Teams, or any other Graph resource. | Nothing in this repo. |
| `Cyclotron.Graph.Mail` | `IGraphMailClient` — messages, well-known folders, delta queries — plus the mail-shaped models and mail-specific option defaults. `AddCyclotronGraphMail` wires up Core for you. | `Cyclotron.Graph.Core` |

## Dependency direction

The dependency is one-way and load-bearing: `Cyclotron.Graph.Mail` references
`Cyclotron.Graph.Core`; `Cyclotron.Graph.Core` never references `Cyclotron.Graph.Mail` or any
other resource-specific package. This is what lets a future sibling package — for example
`Cyclotron.Graph.Teams` — sit on `Cyclotron.Graph.Core` alongside `Cyclotron.Graph.Mail`, reusing
the auth and subscription/notification stack without taking on any mail-specific surface.

## Which package to install

- **Building a mail consumer** (reading, filtering, or reacting to Outlook mail)? Install
  `Cyclotron.Graph.Mail` and call `AddCyclotronGraphMail`. It depends on `Cyclotron.Graph.Core` and
  wires Core up for you with mail-appropriate defaults, in addition to registering
  `IGraphMailClient`. `Cyclotron.Graph.Core` arrives transitively — you never reference it
  directly.
- **Building against Graph subscriptions and change notifications with no workload package** (for
  example, a future `Cyclotron.Graph.Teams` consumer that only needs subscription/notification
  plumbing)? Install `Cyclotron.Graph.Core` directly and call `AddCyclotronGraphCore`. This is a
  genuinely supported, first-class scenario — this package is fully installable on its own,
  matching the `Azure.Core` convention — not a workaround.

See each package's own README for its full registration API, configuration reference, and public
surface: [`Cyclotron.Graph.Core/README.md`](src/Cyclotron.Graph.Core/README.md),
[`Cyclotron.Graph.Mail/README.md`](src/Cyclotron.Graph.Mail/README.md).

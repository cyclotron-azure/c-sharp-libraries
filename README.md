# c-sharp-libraries

Cyclotron's shared C# libraries monorepo. Each subfolder under the repo root is a library
**family** — a set of related NuGet packages that share a dependency direction and a version
lineage but are published as separate packages.

## Library families

| Family | Packages | Status |
|---|---|---|
| `Graph/` | `Cyclotron.Graph.Core`, `Cyclotron.Graph.Mail` | In progress |

## Folder convention

Every family follows the same shape:

```
<Family>/
  src/
    <PackageId>/            — one folder per package, csproj name matches PackageId
  tests/
    <PackageId>.Tests/      — one xUnit test project per package
  samples/                  — optional sample/test-harness project(s) for the family
  README.md                 — family-level overview: package split, dependency direction
```

For example, the `Graph` family's Core package lives at `Graph/src/Cyclotron.Graph.Core/`, its
tests (once added) at `Graph/tests/Cyclotron.Graph.Core.Tests/`, and any sample harness at
`Graph/samples/`.

## Solution

All projects are collected into `CyclotronAzure.Libraries.sln` at the repo root.

## Build

```
dotnet build CyclotronAzure.Libraries.sln
```

## Conventions

- Central package management (`Directory.Packages.props`) — `PackageReference` elements never
  carry a `Version` attribute.
- Shared build properties (`Directory.Build.props`) — target framework, nullable, warnings-as-errors,
  and package metadata are set once for every project in the repo.
- `.editorconfig` is shared with Cyclotron's other repositories for consistent formatting.

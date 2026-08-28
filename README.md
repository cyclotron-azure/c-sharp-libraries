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

```text
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

## Build and test

```sh
dotnet build CyclotronAzure.Libraries.sln
dotnet test CyclotronAzure.Libraries.sln
```

By default a library in this repo compiles against a sibling library's **sources**, so a change to
`Cyclotron.Graph.Core` is validated against `Cyclotron.Graph.Mail`'s tests before either is
released. Packing deliberately does not work this way — see [Versioning](#versioning).

## Versioning

**Every package versions independently.** A package's version comes from git tags carrying its own
prefix, resolved by [MinVer](https://github.com/adamralph/MinVer); the prefix is set in each
package's csproj. No csproj carries a `<Version>` element, and none should.

| Package | Tag prefix | Example release tag |
|---|---|---|
| `Cyclotron.Graph.Core` | `Cyclotron.Graph.Core-v` | `Cyclotron.Graph.Core-v0.0.2` |
| `Cyclotron.Graph.Mail` | `Cyclotron.Graph.Mail-v` | `Cyclotron.Graph.Mail-v0.3.0` |

Given a package's own prefix, MinVer resolves a version from where the commit sits relative to that
package's tags:

| Where you are | Version you get |
|---|---|
| On that package's release tag | the tagged version, e.g. `0.3.0` |
| Commits after it, untagged | `0.3.1-alpha.0.N` (N = commit height) |
| No tag with that prefix yet | `0.0.0-alpha.0.N` |

Releasing Core does not touch Mail's version, and vice versa. Bumping Core to `0.1.0` leaves Mail
at `0.3.0`, still depending on the Core it was built against.

### Why packing uses package references

Independent versions and project references cannot coexist. `<ProjectReference>` makes the shipped
dependency version *whatever the sibling computes in that same build* — and because MinVer counts
commit height across the whole repo, a sibling is on a prerelease at any commit that isn't exactly
its own release tag. Packing stable `Cyclotron.Graph.Mail` 0.3.0 that way emits a dependency on a
prerelease `Cyclotron.Graph.Core` that was never published, and NuGet rejects it outright:

```text
error NU5104: A stable release of a package should not have a prerelease dependency.
```

So the two reference modes are split by purpose, via the `UseProjectReferences` property:

| Mode | Used for | Mail depends on |
|---|---|---|
| `UseProjectReferences=true` (default) | local builds, CI build + test | Core's **sources** |
| `UseProjectReferences=false` | packing and publishing | the **published** Core pinned in `Directory.Packages.props` |

`Directory.Build.props` fails the pack with an explicit error if you try to pack in project-reference
mode, so a wrong-dependency package cannot be produced by accident.

**Bumping a sibling dependency is therefore a deliberate edit.** When Mail should require a newer
Core, publish that Core first, then raise its pin:

```xml
<PackageVersion Include="Cyclotron.Graph.Core" Version="0.1.0" />
```

The pin is a minimum (`>= 0.1.0`), not an exact match, which is the normal NuGet convention — a
consumer resolves that version by default but may be moved higher by a direct reference of its own.

## Packing

```sh
dotnet pack Graph/src/Cyclotron.Graph.Core/Cyclotron.Graph.Core.csproj   -c Release -o artifacts -p:UseProjectReferences=false
```

The flag is required: without it the pack guard stops you. Every version pinned in
`Directory.Packages.props` must already exist on the feed for the restore to succeed, so **the
first release of a new package must precede the first release of anything that depends on it**.

Only `src/` projects pack; tests and samples set `IsPackable=false`. PDBs are embedded in the
assemblies rather than shipped as `.snupkg`, because GitHub Packages has no symbol server — with
SourceLink that still gives consumers full source stepping, with nothing extra to host. Builds
under CI are deterministic.

### Credentials for local packing

Building and testing needs no credentials: source mapping confines the GitHub feed to
`Cyclotron.*`, and sibling libraries build from source, so nothing is ever requested from it. Only
packing is different — it restores the published sibling and therefore needs a token with
`read:packages`.

Supply it through the environment, never through the file:

```sh
# bash
export NuGetPackageSourceCredentials_github="Username=<your-github-user>;Password=<pat>"
```

```powershell
# PowerShell
$env:NuGetPackageSourceCredentials_github = "Username=<your-github-user>;Password=<pat>"
```

Do **not** use `dotnet nuget update source github --store-password-in-clear-text` locally: this
repo's `nuget.config` is tracked, so that writes your PAT into a file git is watching. CI can use
it safely only because the runner's copy is thrown away.

## Continuous integration

Two workflows, neither of them per-package. Adding a library never means adding a workflow.

| Workflow | Trigger | Does |
|---|---|---|
| [`ci.yml`](.github/workflows/ci.yml) | pull requests, pushes to `main` | builds and tests the whole solution on `ubuntu-latest`. Publishes nothing. |
| [`release.yml`](.github/workflows/release.yml) | tag `Cyclotron.*-v*` | derives the package from the tag, builds and tests the repo, then packs and pushes **only that package** |

Build and test deliberately run against the solution rather than one package: the value of a
monorepo is that a change to `Cyclotron.Graph.Core` is checked against `Cyclotron.Graph.Mail`'s
tests in the same run, which a per-package pipeline cannot do.

CI runs on Linux only, because Linux is what we deploy on. Cross-platform differences are real in
this code — `Uri.TryCreate(UriKind.Absolute)` accepts a rooted path such as `/notifications` on
Linux but rejects it on Windows, which produced a validator bug — but they are already covered
from both sides: CI gates the deployment platform, and developer machines are Windows, so
Windows-only breakage shows up locally as soon as it is written. A second runner would bill 2x
Linux minutes on an internal repo to gate what is exercised every day anyway.

`ci.yml` needs no feed credentials at all: sibling libraries build from source and source mapping
means no `Cyclotron.*` package is ever requested, so it works on pull requests from forks.

## Publishing

Packages are hosted on **GitHub Packages** under the `cyclotron-azure` organization:

```text
https://nuget.pkg.github.com/cyclotron-azure/index.json
```

Releases are tag-driven, and only the tagged package is published — nothing else in the repo is
republished, and no prereleases are pushed from `main`. `release.yml` authenticates with the
built-in `GITHUB_TOKEN`, for restore as well as push; no PAT or repository secret is needed.

```sh
git tag Cyclotron.Graph.Core-v0.0.2
git push origin Cyclotron.Graph.Core-v0.0.2
```

The workflow parses the package id and version straight out of the tag, and fails before pushing
if MinVer resolves a version that disagrees with it. The tag must point at a commit that already
contains the workflow, so push your commits first.

There is no "cut a release" button, by design — pushing the tag *is* the release. The **Run
workflow** button on `release.yml` exists only to re-run an **existing** tag (say a push step
failed after the tag was already created); it takes the tag as an input and rejects anything that
is not one, so it can never invent a version that has no tag behind it.

## Consuming these packages

GitHub Packages requires authentication for NuGet **even for public packages**, so every
consumer — developer machines, CI, container builds — needs a token with `read:packages`. In the
consuming repo:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="cyclotron" value="https://nuget.pkg.github.com/cyclotron-azure/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <cyclotron>
      <add key="Username" value="%GITHUB_USER%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </cyclotron>
  </packageSourceCredentials>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="cyclotron">
      <package pattern="Cyclotron.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

The `packageSourceMapping` block is mandatory if the consuming repo uses central package
management — NuGet fails restore with `NU1507` when more than one source is defined without it —
and is worth keeping either way, since it means a `Cyclotron.*` package can only ever resolve from
our feed and never from a nuget.org name squatter.

Keep the token in the environment, not in a committed `nuget.config`.

## Conventions

- Central package management (`Directory.Packages.props`) — `PackageReference` elements never
  carry a `Version` attribute. This includes the pinned versions of sibling packages in this repo.
- Shared build properties (`Directory.Build.props`) — target framework, nullable, warnings-as-errors,
  and package metadata are set once for every project in the repo. Versioning is per-package: each
  packable csproj sets its own `MinVerTagPrefix`.
- Package sources (`nuget.config`) — two sources with package source mapping: `Cyclotron.*`
  resolves only from GitHub Packages, everything else only from nuget.org. The mapping is required
  by central package management and never contacted by a normal local build.
- `.editorconfig` is shared with Cyclotron's other repositories for consistent formatting.

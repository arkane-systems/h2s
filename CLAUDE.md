# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

h2s is a single-project intranet link dashboard: ASP.NET Core Razor Pages on .NET 10, EF Core + SQLite, Bootstrap 5.3.

## Commands

Run from the repository root:

- Build: `dotnet build h2s/h2s.csproj`
- Create the dev database with sample data: `./scripts/new-dev-db.ps1` or `scripts/new-dev-db.sh`. Add `-Reset` / `--reset` to replace an existing one, and `-From <path>` / `--from <path>` to snapshot an existing database (e.g. a prod copy) instead of seeding. Both wrap `dotnet run --project h2s/h2s.csproj -- seed [--reset] [--from <path>]`, which is Development-only.
- Run (http://localhost:5284, Development): `dotnet run --project h2s/h2s.csproj`
- Add migration: `dotnet ef migrations add <Name> --project h2s/h2s.csproj --startup-project h2s/h2s.csproj --output-dir Migrations`
- Apply migrations manually: `dotnet ef database update --project h2s/h2s.csproj --startup-project h2s/h2s.csproj` (normally unnecessary: `Program.cs` calls `Database.Migrate()` on startup)
- Container: `docker build -t h2s:local -f h2s/Dockerfile h2s` (build context is `h2s/`, not the repo root)

- Test: `dotnet test --solution h2s.slnx`. For a single test, add `--filter-method "*Post_SavesValidSettings"`; `--filter-class` and `--filter-namespace` also work.

Tests are xUnit v3 in `tests/h2s.Tests`, run on Microsoft.Testing.Platform. `global.json` opts `dotnet test` into MTP, which the .NET 10 SDK requires for xUnit v3, so `dotnet test` takes `--solution`/`--project` rather than a positional path.

- `Unit/` tests use `TestDatabase`, a migrated in-memory SQLite database, optionally seeded.
- `Integration/` tests host the app with `DashboardApplicationFactory` over a private in-memory database seeded by `DevDataSeeder`. Create one factory per test that changes data.
- Posting to Razor handlers needs an antiforgery token; use `FormPoster.PostAsync`.
- Pass `TestContext.Current.CancellationToken` to async calls; the xUnit analyzers flag it otherwise.

Code style:
- Restore the pinned tools (`jb`, `dotnet-ef`) with `dotnet tool restore`; they're listed in `dotnet-tools.json`.
- Format with `dotnet jb cleanupcode h2s.slnx` (Built-in: Full Cleanup). Add `--include="path/To/File.cs"` to limit it. It takes a couple of minutes because it loads the whole solution.
- `dotnet jb inspectcode h2s.slnx --output=<file> --format=Sarif` runs the ReSharper inspections.
- Don't run `dotnet format whitespace`; it fights the ReSharper alignment rules.
- The build reports the `.editorconfig` rules as warnings (`EnforceCodeStyleInBuild`). `-p:ContinuousIntegrationBuild=true` makes all warnings errors.

The connection string key is `ConnectionStrings:h2s`. Development uses `App_Data/h2s.db`. `Program.cs` resolves a relative `Data Source` against the content root (`SqliteConnectionStrings`), giving `h2s/App_Data/h2s.db`, which is gitignored and dockerignored. Production uses `/app/data/h2s.db`, a mounted volume. Don't name a data directory `data`: on Windows it collides with the `h2s/Data/` source folder. Sample data lives in `h2s/Data/Seeding/DevDataSeeder.cs`; keep it in step with schema changes, since the AddDashboardSettings migration inserts the settings row and the seeder updates it. Any new environment keeps the same connection string key and the SQLite provider. The app is single-instance only: SQLite with no concurrency support.

## Architecture

- **Data**: one `DashboardContext` (`h2s/Data/`) with `Categories`, `Links`, `InfrastructureGroups` and `DashboardSettings`.
  - `DashboardSettings` is a singleton row enforced by a check constraint (`Id = 1`). Always read and write it through `DashboardSettingsService`. If the row is missing, the service creates it with the defaults `Title = "Dashboard"`, empty `Motto` and `LocalDomains`, `ColorMode = Auto` and `UptimeKumaDefaultDuration = 24`. It also resets a stored duration of 0 or less to 24.
  - Categories and links are accessed directly through `DashboardContext` in the PageModels, with no repository layer. Don't add a service for them without a clear need.
- **Schema changes**: update the model, `DashboardContext` and a new migration together. Migrations are applied automatically at startup, including in production.
- **Pages**:
  - `Pages/Index` is the public dashboard.
    - Categories sort by `IsAdminCategory`, then `Name`; admin categories render after a grid break, tinted red.
    - Links within a category sort by `Label`.
    - Each link shows a selfh.st icon when `IconName` is set, otherwise a fallback dot. Links whose host isn't in `LocalDomains` get an "External" badge.
    - A Bing search box sits at the top; keep additions visually compatible with the card/grid layout.
  - `Pages/Shared/_Layout.cshtml` holds the navbar, including the links to Editor and Settings. It injects `DashboardSettingsService` directly for the title, motto and theme. It also holds the inline pre-CSS theme script and the Trianglify background renderer.
  - `Pages/Admin/*` (Editor, Settings, ToggleColorMode) are unauthenticated by design for now.
- **Editor and Settings are JSON-handler pages, not scaffolded CRUD.** The server renders the initial state, then `wwwroot/js/editor.js` / `wwwroot/js/infrastructure-groups.js` call named page handlers (`OnPostCreateCategoryAsync`, `OnPostUpdateLinkAsync`, `OnPostReorderInfrastructureGroupsAsync`, …) via `fetch`. When you change editor or settings behavior, update the Razor markup, the PageModel handler and the JS contract together.
- **Color mode**: the `ColorMode` enum has the values `Auto` (0, follows `prefers-color-scheme`), `Light` (1) and `Dark` (2).
  - An inline script in the `<head>` of `_Layout.cshtml` sets `data-bs-theme` on `<html>` before CSS loads, so the wrong theme never flashes on load.
  - The navbar toggle posts to `/Admin/ToggleColorMode`, which cycles the mode and returns JSON. The client then updates `data-bs-theme` and re-renders Trianglify.
- **Uptime Kuma integration**: enabled only when `DashboardSettings.UptimeKumaServerUrl` is non-blank.
  - The browser loads badges directly from Kuma as `<img>` tags pointing at `{server}/api/badge/{monitorId}/{type}` (built in `IndexModel.BuildBadgeUrl`); the server never proxies them.
  - Infrastructure groups render the top status bar. The bar links to the Kuma status page only when `UptimeKumaStatusPageSlug` is set; otherwise it isn't clickable.
  - `Link.MonitorId` renders per-link badges.
  - Monitors must be on a published Kuma status page or the badge shows N/A.
- **Outbound HTTP**:
  - The Settings page checks that the Kuma URL is reachable through `IHttpClientFactory`. On a TLS auth failure it re-probes without certificate validation, so it can report "untrusted certificate" rather than "unreachable".
  - The Editor checks whether a selfh.st CDN icon exists through its own static `HttpClient` and caches the results in `IMemoryCache`. Keep that caching if you extend icon suggestion.
- **Icons**: not stored locally. `Link.NormalizeIconName()` / `BuildIconUrl()` / `GetIconUrl()` produce selfh.st CDN URLs.
- **Validation**: custom attributes live in `h2s/Validation/` (`HttpBaseUrlAttribute`, `PositiveIntegerStringAttribute`).
  - `Program.cs` sets `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes`, so form-bound models must mark required fields with an explicit `[Required]`.
  - Optional non-nullable strings need `[DisplayFormat(ConvertEmptyStringToNull = false)]`. Otherwise an empty field binds as `null`.

## Conventions

- Keep edits minimal and local.
- Stay within Razor Pages: no MVC controllers, no Blazor, no new frontend frameworks. Site styling goes in `h2s/wwwroot/css/site.css`.
- New services go in `h2s/Services/` and are registered as scoped in `Program.cs` unless they clearly need another lifetime. Keep the `AddHttpClient()`, `MapStaticAssets()` and `MapRazorPages().WithStaticAssets()` calls.
- Namespaces follow folders: `h2s.Data`, `h2s.Models`, `h2s.Services`, `h2s.Validation`, `h2s.Pages`, `h2s.Pages.Admin`.
- C# style: the root `.editorconfig` is the source of truth. Its ReSharper section is generated from the house ReSharper profile. `h2s.slnx.DotSettings` holds what EditorConfig can't express: the member layout pattern and XML formatter settings. `Directory.Build.props` holds the shared project settings. In short:
  - 2-space indent, Allman braces, and a space before parentheses in calls and declarations (`Foo ()`). Aligned assignments, initializers and multiline arguments, and trailing commas in multiline lists.
  - Qualify instance members with `this.` and static members with their type name (`Link.NormalizeIconName`).
  - Private fields are camelCase with **no** `_` prefix. Constants and static readonly fields are PascalCase.
  - `var` only when the type is evident.
  - File-scoped namespaces, with usings outside them. `JetBrains.Annotations` is a global using.
  - XML doc comments on public members (CS1591 is on, except in tests).
  - Named arguments for literal and other non-trivial arguments; single-argument calls stay positional.
  - Braces are dropped from single-statement bodies, so put comments above an `if`, never between it and its statement.
- Full Cleanup isn't idempotent in one pass: it can drop braces, then wrap the statement, then restore the braces on the next pass. After cleaning new code, run it again until `git diff` is empty.
- Purely mechanical formatting commits go in `.git-blame-ignore-revs`. Locally, `git config blame.ignoreRevsFile .git-blame-ignore-revs`.
- Dark mode:
  - Prefer Bootstrap theme-aware utilities (`bg-body`, `bg-body-tertiary`, `text-body`, `text-muted`, `border`) and CSS variables (`var(--bs-body-bg)`, `var(--bs-border-color)`, `rgba(var(--bs-body-bg-rgb), …)`) over hard-coded colors.
  - Scope theme-specific overrides with `[data-bs-theme="dark"]` / `[data-bs-theme="light"]`.
  - JS visuals read `document.documentElement.getAttribute('data-bs-theme')` and re-render when it changes; the Trianglify background is the reference implementation.
  - Translucent surfaces follow the existing `category-box`, `link-block` and `home-search-form` styles.

## CI and release

`.github/workflows/ci.yml` runs on pushes and PRs to `dev` and `master`. It has two parallel jobs:
- **Build and test:** a Release build with `ContinuousIntegrationBuild=true` (warnings are errors), then `dotnet test`.
- **Code style and inspections:** `jb cleanupcode` must leave `git diff` empty. On failure, a `cleanup-patch` artifact holds the required changes. `jb inspectcode` then uploads an `inspectcode-sarif` artifact; it's informational and doesn't fail the build.

The style job is the slow one, at roughly 5–10 minutes.

`.github/workflows/publish-container.yml` publishes `ghcr.io/arkane-systems/h2s:<tag>` and `:latest` on tag push, but only if the tagged commit is on `master`. It first runs `ci.yml` as a reusable workflow and publishes only if that passes. Manual dispatch uses the latest tag.

Day-to-day work happens on feature branches off `dev`, one PR into `dev` per change, merged with merge commits (not squash, which would invalidate `.git-blame-ignore-revs`). `dev` merges to `master` for release.

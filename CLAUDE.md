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
- Container: `docker build -t h2s:local -f h2s/Dockerfile .` (or `wslc build` on Windows). The build context is the repo root, so `Directory.Build.props` and `.editorconfig` apply; the root `.dockerignore` limits what is sent.
  - The build stage publishes for `$TARGETARCH`, so only that architecture's native SQLite library ships.
  - The image runs as `$APP_UID` (1654). Deployments may override the UID to match the data volume, so nothing outside `/app/data` may need to be writable.
  - `Microsoft.EntityFrameworkCore.Tools` is `Publish="false"`: `dotnet ef` needs it in the build output, but its Roslyn and MSBuild dependencies stay out of the image. Don't add design-time packages without the same treatment.

- Test: `dotnet test --solution h2s.slnx`. For a single test, add `--filter-method "*Post_SavesValidSettings"`; `--filter-class` and `--filter-namespace` also work.

Tests are xUnit v3 in `tests/h2s.Tests`, run on Microsoft.Testing.Platform. `global.json` opts `dotnet test` into MTP, which the .NET 10 SDK requires for xUnit v3, so `dotnet test` takes `--solution`/`--project` rather than a positional path.

- `Unit/` tests use `TestDatabase`, a migrated in-memory SQLite database, optionally seeded.
- `Integration/` tests host the app with `DashboardApplicationFactory` over a private in-memory database seeded by `DevDataSeeder`, with Data Protection keys held in memory. Create one factory per test that changes data.
- Posting to Razor handlers needs an antiforgery token; use `FormPoster.PostAsync`.
- Pass `TestContext.Current.CancellationToken` to async calls; the xUnit analyzers flag it otherwise.

Code style:
- Restore the pinned tools (`jb`, `dotnet-ef`) with `dotnet tool restore`; they're listed in `dotnet-tools.json`.
- Format exactly as CI does with `dotnet jb cleanupcode h2s.slnx --include="**/*.cs" --exclude="**/Migrations/**" --disable-settings-layers="GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal"` (Built-in: Full Cleanup).
  - Narrow `--include` to a file to limit it.
  - It takes about 5 minutes because it loads the whole solution.
  - Disabling the machine-global layers keeps results reproducible. Repo settings already override them, so the IDE agrees anyway.
- `dotnet jb inspectcode h2s.slnx --output=<file> --format=Sarif` runs the ReSharper inspections.
- Don't run `dotnet format whitespace`; it fights the ReSharper alignment rules.
- The build reports the `.editorconfig` rules as warnings (`EnforceCodeStyleInBuild`). `-p:ContinuousIntegrationBuild=true` makes all warnings errors.

The connection string key is `ConnectionStrings:h2s`. Development uses `App_Data/h2s.db`. `Program.cs` resolves a relative `Data Source` against the content root (`SqliteConnectionStrings`), giving `h2s/App_Data/h2s.db`, which is gitignored and dockerignored. Production uses `/app/data/h2s.db`, a mounted volume. Don't name a data directory `data`: on Windows it collides with the `h2s/Data/` source folder. Data Protection keys persist to a `keys` directory beside the database (`SqliteConnectionStrings.GetDataDirectory`), so antiforgery tokens survive restarts. Sample data lives in `h2s/Data/Seeding/DevDataSeeder.cs`; keep it in step with schema changes, since the AddDashboardSettings migration inserts the settings row and the seeder updates it. Any new environment keeps the same connection string key and the SQLite provider. The app is single-instance only: SQLite with no concurrency support.

## Architecture

- **Data**: one `DashboardContext` (`h2s/Data/`) with `Categories`, `Links`, `InfrastructureGroups` and `DashboardSettings`.
  - `DashboardSettings` is a singleton row enforced by a check constraint (`Id = 1`). Always read and write it through `DashboardSettingsService`. If the row is missing, the service creates it with the defaults `Title = "Dashboard"`, empty `Motto` and `LocalDomains`, `ColorMode = Auto` and `UptimeKumaDefaultDuration = 24`. It returns 24 in place of a stored duration of 0 or less, without writing it back.
  - The service caches the settings in `IMemoryCache` (5-minute expiry) and hands out copies, which callers may change and pass to `SaveSettingsAsync`. Saving invalidates the cache. Writes that bypass the service (the seeder, `ExecuteUpdate` in tests) show up only after expiry, so in integration tests change settings before the first request.
  - Categories and links are accessed directly through `DashboardContext` in the PageModels, with no repository layer. Don't add a service for them without a clear need.
- **Schema changes**: update the model, `DashboardContext` and a new migration together. Migrations are applied automatically at startup, including in production.
- **Pages**:
  - `Pages/Index` is the public dashboard.
    - Categories sort by `IsAdminCategory`, then `SortOrder` (then `Name`); admin categories render after a grid break, tinted red. `SortOrder` counts within a section, so regular and admin categories are ordered separately.
    - Links within a category sort by `SortOrder` (then `Label`). New links, and links or categories moved to another category or section, go to the end.
    - **Edit mode** (`/?edit=true`, granted only when the `Admin` policy passes; the navbar's Edit/Done button toggles it) adds drag handles. `wwwroot/js/modules/edit-mode.js` uses the vendored SortableJS to reorder categories within their section and links within or across categories, and arrow keys on a focused handle do the same. Each move posts the full ordered ID list to `OnPostReorderCategoriesAsync` / `OnPostReorderLinksAsync` on the Editor page, which renumber `SortOrder` from 0; on failure the page puts things back and shows a toast.
    - Edit mode also adds, edits and deletes links and categories through the shared `_LinkDialog` (`link-dialog.js`) and `_CategoryDialog` (`category-dialog.js`) partials, calling the Editor page's handlers. Tiles render as `_EditableLinkSlot`, and after a save the page swaps in a fresh one from `OnGetLinkSlotAsync`. The dialog's live preview is `_LinkTile` itself, rendered by `OnGetTilePreviewAsync`, so it can't drift from the dashboard. Adding a category, or moving one between sections, reloads the page.
    - Deletes return a snapshot (`LinkSnapshot` / `CategorySnapshot` in `Pages/Admin/`). The toast's Undo posts it unchanged to `OnPostRestoreLinkAsync` / `OnPostRestoreCategoryAsync`, which recreate the item (with new IDs) at its old position.
    - Each link shows a selfh.st icon when `IconName` is set, otherwise a fallback dot. Links whose host isn't in `LocalDomains` get an "External" badge.
    - Tiles render through the `Pages/Shared/_LinkTile` partial. Its `LinkTile` view model comes from `LinkTileFactory` (`Pages/`), which applies the settings' local domains and Kuma URL; `IndexModel` and the Editor handlers both use it. The Kuma status bar is the `_UptimeStatusBar` partial.
    - A Bing search box sits at the top; keep additions visually compatible with the card/grid layout.
  - `Pages/Shared/_Layout.cshtml` holds the navbar: theme toggle, Settings, then Edit/Done at the far right. Done shows in edit mode and in the list view (`ViewData["IsEditMode"]` / `ViewData["IsListView"]`) and returns to the dashboard. The list view is reached from the edit-mode banner, not the navbar. It injects `DashboardSettingsService` directly for the title, motto and theme. It also holds the inline pre-CSS theme script and the background renderer, which draws through `wwwroot/js/lowpoly.js`, a small dependency-free replacement for Trianglify (Trianglify is GPL-3.0). The layout renders the page's one antiforgery token for `api.js`, and the `#toast-host` container used by `wwwroot/js/modules/toast.js`.
    - Modal dialogs go in the layout's `Dialogs` section, never inside the page body. `.app-content` has a `z-index`, so a modal inside it is trapped under Bootstrap's backdrop and can't be clicked.
  - `Pages/Admin/*` (Editor, Settings, ToggleColorMode) require the `Admin` policy (`AuthorizeFolder` in `Program.cs`). Its only requirement, `AdminRequirement`, is satisfied for everyone by `AllowAllAdminHandler` (`h2s/Authorization/`). That's the authorization seam: authentication will come from a homelab-wide project, which replaces the handler. Don't add login or authentication to h2s itself.
- **Hosting**:
  - `/healthz` is a health check (DbContext) for Kubernetes liveness and readiness probes.
  - `UseForwardedHeaders` runs first and trusts `X-Forwarded-For`/`-Proto` from any address, because the app is only reachable through Traefik.
- **Editor and Settings are JSON-handler pages, not scaffolded CRUD.** The server renders the initial state, then client scripts call named page handlers (`OnPostCreateCategoryAsync`, `OnPostUpdateLinkAsync`, `OnPostReorderInfrastructureGroupsAsync`, …). When you change editor or settings behavior, update the Razor markup, the PageModel handler and the JS contract together.
  - `/Admin/Editor` is the **list view**: `wwwroot/js/modules/link-list.js` renders a filterable, sortable table of links from the page's embedded JSON (`LinkRow`s), and refreshes it from `OnGetLinksAsync` after every change. Rows open the shared link dialog; bulk move and delete go to `OnPostMoveLinksAsync` / `OnPostDeleteLinksAsync`. Bulk-delete Undo restores the returned snapshots one at a time, in the order given, through `OnPostRestoreLinkAsync`. The page also hosts every handler edit mode's dialogs call, so keep its URL. Categories are managed only in edit mode.
  - **Settings** is a page of section cards (General, Appearance, Integrations, Backup). Each one posts to its own JSON handler, and nothing posts the whole page:
    - `settings.js` drives the cards: `OnPostSaveGeneralAsync`, `OnPostSaveAppearanceAsync` and `OnPostSaveUptimeKumaAsync`. Each validates its fields and returns ProblemDetails, and saves through `DashboardSettingsService`. After a save, the navbar title and motto update in place, and Appearance applies the theme through `window.h2sColorMode.apply`, which the layout exposes.
    - **Test connection** (`OnPostTestUptimeKumaAsync`) checks the form's values without saving. It probes reachability and certificate trust with `UrlProbe`, then the status page through `UptimeKumaClient`, and answers a `level` and a `message`.
    - **Local domains** use `chip-input.js`; the posted value stays the comma-separated string the server already parses. Status page slugs are checked by `StatusPageSlug` (`h2s/Validation/`), which import uses too.
    - **Infrastructure groups** (`modules/infrastructure-groups.js`) render from JSON, reorder with SortableJS or arrow keys, and edit in `_InfrastructureGroupDialog` with the monitor picker. Delete returns what `OnPostRestoreInfrastructureGroupAsync` needs for Undo.
    - The page model no longer binds a form: `DashboardSettings`' validation attributes aren't used for binding any more, and the handlers do the checking.
  - **Settings → Backup** (`wwwroot/js/modules/backup.js`) exports through `OnGetExportAsync` and imports in two steps: `OnPostPreviewImportAsync` checks the uploaded file and summarizes it, and `OnPostImportAsync` replaces everything. `DashboardBackupService` (`h2s/Services/`) does the work: `ExportAsync`, `TryRead` (format, version and field checks, up to ten errors, one per line in the problem detail), `Summarize`, and `ImportAsync`, which runs in one transaction and saves settings through `DashboardSettingsService` so the cache is cleared. The file format (`DashboardBackup` and its `Backup*` records) is versioned (`h2s-dashboard`, version 1). Bump the version and keep reading old files when it changes, and keep secrets (Phase 5) out of it.
  - `h2sApi.postForm` posts a `FormData` (such as a file upload) as multipart; anything else form-encoded.
  - Client scripts call handlers through `wwwroot/js/api.js` (`h2sApi.getJson`, `h2sApi.postForm`), which sends the antiforgery token and turns error responses into `Error`s carrying the problem's `detail`.
  - There's no jQuery or jquery-validation: forms use native constraint validation (`required`, `pattern`, `type="url"`, Bootstrap's `was-validated`), and the server's ProblemDetails show inline. Don't bring them back.
  - New client code is no-build ES modules under `wwwroot/js/modules/`, loaded with `<script type="module">`; the layout's import map resolves relative imports to fingerprinted assets. Reorder handlers parse their ID lists with `OrderedIdParser` (`h2s/Validation/`).
  - JSON handlers report failures as RFC 9457 ProblemDetails (`application/problem+json`) via `this.BadRequestProblem (...)` / `this.NotFoundProblem (...)` (`h2s/Pages/PageModelProblemExtensions.cs`), not bare `BadRequest`/`NotFound`. Tests check them with `FormPoster.AssertProblemAsync`.
- **Color mode**: the `ColorMode` enum has the values `Auto` (0, follows `prefers-color-scheme`), `Light` (1) and `Dark` (2).
  - An inline script in the `<head>` of `_Layout.cshtml` sets `data-bs-theme` on `<html>` before CSS loads, so the wrong theme never flashes on load.
  - The navbar toggle posts to `/Admin/ToggleColorMode`, which cycles the mode and returns JSON. The client then updates `data-bs-theme` and re-renders the background.
- **Uptime Kuma integration**: enabled only when `DashboardSettings.UptimeKumaServerUrl` is non-blank.
  - The browser loads badges directly from Kuma as `<img>` tags pointing at `{server}/api/badge/{monitorId}/{type}` (built in `IndexModel.BuildBadgeUrl`); the server never proxies them.
  - Infrastructure groups render the top status bar. The bar links to the Kuma status page only when `UptimeKumaStatusPageSlug` is set; otherwise it isn't clickable.
  - `Link.MonitorId` (`int?`) renders per-link badges; `InfrastructureGroup.MonitorId` is a required `int`.
  - Monitors must be on a published Kuma status page or the badge shows N/A.
  - The link dialog's monitor picker (`monitor-picker.js`) lists that status page's monitors through `UptimeKumaClient` (`h2s/Services/`). It calls Kuma's public `GET {server}/api/status-page/{slug}` (`publicGroupList[].monitorList[].{id,name}`) and caches successful reads for 2 minutes. The Editor's `OnGetKumaMonitorsAsync` always answers 200, with a `message` instead of groups when Kuma or its slug isn't configured or can't be read; the picker then falls back to typing an ID. The posted field is still the plain `monitorId` input.
- **Outbound HTTP** goes through named `IHttpClientFactory` clients registered in `Program.cs`, each with its timeout:
  - `UrlProbe` (`h2s/Services/`) checks that a URL is reachable, used by the Settings page for the Kuma URL. On a TLS auth failure it re-probes with the `UrlProbe.AcceptAnyCertificate` client, so it can report "untrusted certificate" rather than "unreachable". Never use that client to fetch content.
  - `IconCatalog` (`h2s/Services/`) suggests selfh.st icons by matching names against the CDN's `index.json`, and `SearchAsync` ranks icons for the dialog's icon picker (`icon-picker.js`, via `OnGetSearchIconsAsync`). It downloads the index once and caches it in `IMemoryCache` for 12 hours, retrying after a minute if the download failed. Keep that caching if you extend icon suggestion.
  - `PageMetadataReader` (`h2s/Services/`) reads what the link dialog suggests. People enter base URLs, so it follows HTTP redirects (at most five) and then up to two client-side redirects: a meta refresh, or a stub page's script `location` set to a string literal. It follows those from error pages too, which Plex needs (a 401 page with a script redirect), but never uses an error page's title.
    - It also finds the site's icon: the largest declared `<link rel="icon">`, `apple-touch-icon` or manifest icon (mask icons and maskable-only icons skipped, scalable counting as 512px), else `/favicon.ico` if it serves something other than HTML. The dialog offers it as `IconUrl` only when there's no selfh.st icon.
    - On the final pages it collects a title, cleaned by `CleanTitle` (generic parts like "Login", "Dashboard", "…sign in" and colon taglines dropped), and app names from the web app manifest (`short_name`, `name`) and the `application-name`, `apple-mobile-web-app-title` and `og:site_name` meta tags.
    - The label is the title, or else the first app name. `OnGetSuggestLinkAsync` matches icons against the label, the app names, the title and its parts, the host, and finally first words.
    - It fetches only http(s) URLs, reads at most 256 KB per page (64 KB per manifest), and caches only useful results.
  - Server-side fetches of internal hosts keep full certificate validation. A homelab CA is trusted by mounting it and adding its directory to `SSL_CERT_DIR` (see the README), never by switching to the accept-any client.
  - Tests replace the network with `StubHttpClientFactory` and `StubHttpMessageHandler` (`tests/h2s.Tests/Infrastructure/`). `DashboardApplicationFactory` stubs the `IconCatalog` and `PageMetadata` clients. By default it serves a small icon index (`IconIndexJson`) and 404 elsewhere; set `Outbound` before the first request to change that.
- **Icons**: not stored locally. A link shows its selfh.st icon (`IconName`) if it has one, else its own `IconUrl` (such as a favicon), else a dot; `Link.GetIconUrl()` applies that order. `Link.NormalizeIconName()` / `BuildIconUrl()` produce selfh.st CDN URLs. `wwwroot/js/site.js` swaps a tile icon that fails to load for the dot. The older Editor page doesn't post `iconUrl`, and `OnPostUpdateLinkAsync` keeps the stored value when the field is absent, as it does for `monitorId`.
- **Validation**: custom validation lives in `h2s/Validation/`: `HttpBaseUrlAttribute`, and `MonitorIdParser`, which the JSON handlers use to parse posted monitor IDs (positive integers in plain digits; blank means none where the ID is optional).
  - `Program.cs` sets `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes`, so form-bound models must mark required fields with an explicit `[Required]`.
  - Optional non-nullable strings need `[DisplayFormat(ConvertEmptyStringToNull = false)]`. Otherwise an empty field binds as `null`.

## Conventions

- Keep edits minimal and local.
- Stay within Razor Pages: no MVC controllers, no Blazor, no new frontend frameworks. Site styling goes in `h2s/wwwroot/css/site.css`.
- Client libraries are committed under `h2s/wwwroot/lib/`; don't load scripts from a CDN at runtime. h2s is MIT, so check a library's licence before vendoring it (no GPL).
- New services go in `h2s/Services/` and are registered as scoped in `Program.cs` unless they clearly need another lifetime. Keep the `AddHttpClient()`, `MapStaticAssets()` and `MapRazorPages().WithStaticAssets()` calls.
- Namespaces follow folders: `h2s.Authorization`, `h2s.Data`, `h2s.Models`, `h2s.Services`, `h2s.Validation`, `h2s.Pages`, `h2s.Pages.Admin`.
- C# style: the root `.editorconfig` is the source of truth. Its ReSharper section is generated from the house ReSharper profile. `h2s.slnx.DotSettings` is the team-shared ReSharper layer. It holds the full house code-style settings, taken from the author's global ReSharper settings because the profile export alone was incomplete. It also holds what EditorConfig can't express: the member layout pattern and XML formatter settings. ReSharper may append `SettingsMigration` markers to it. Watch for migrations rewriting the layout pattern; one once swapped in a `#region`-based pattern, and the house pattern uses no regions. `Directory.Build.props` holds the shared project settings. In short:
  - 2-space indent, Allman braces, and a space before parentheses in calls and declarations (`Foo ()`). Aligned assignments, initializers and multiline arguments, and trailing commas in multiline lists. Lines wrap at 132 columns.
  - Qualify instance members with `this.` and static members with their type name (`Link.NormalizeIconName`).
  - Private fields are camelCase with **no** `_` prefix. Constants and static readonly fields are PascalCase.
  - `var` only when the type is evident.
  - File-scoped namespaces, with usings outside them. `JetBrains.Annotations` is a global using.
  - XML doc comments on public members (CS1591 is on, except in tests).
  - Named arguments for literal and other non-trivial arguments; single-argument calls stay positional.
  - Expression bodies are preferred for methods, operators and constructors. Braces only when a body spans multiple lines, so put comments above an `if`, never between it and its statement.
- Full Cleanup isn't idempotent in one pass: it can drop braces, then wrap the statement, then restore the braces on the next pass. After cleaning new code, run it again until `git diff` is empty.
- Purely mechanical formatting commits go in `.git-blame-ignore-revs`. Locally, `git config blame.ignoreRevsFile .git-blame-ignore-revs`.
- Dark mode:
  - Prefer Bootstrap theme-aware utilities (`bg-body`, `bg-body-tertiary`, `text-body`, `text-muted`, `border`) and CSS variables (`var(--bs-body-bg)`, `var(--bs-border-color)`, `rgba(var(--bs-body-bg-rgb), …)`) over hard-coded colors.
  - Scope theme-specific overrides with `[data-bs-theme="dark"]` / `[data-bs-theme="light"]`.
  - JS visuals read `document.documentElement.getAttribute('data-bs-theme')` and re-render when it changes; the low-poly background is the reference implementation.
  - The background also follows the page's height, because pages like the list view build their content in script after it is first drawn. A `ResizeObserver` on `<body>` resizes it, measuring with the background collapsed so the page can shrink. `lowpoly.js` takes a per-page-load `seed` and hashes each grid position, so a redraw at a new size keeps the existing triangles; only the colour gradients stretch.
  - Translucent surfaces follow the existing `category-box`, `link-block` and `home-search-form` styles.

## CI and release

`.github/workflows/ci.yml` runs on pushes and PRs to `dev` and `master`. It has two parallel jobs:
- **Build and test:** a Release build with `ContinuousIntegrationBuild=true` (warnings are errors), then `dotnet test`.
- **Code style and inspections:** `jb cleanupcode` must leave `git diff` empty. On failure, a `cleanup-patch` artifact holds the required changes. `jb inspectcode` then uploads an `inspectcode-sarif` artifact; it's informational and doesn't fail the build.

The style job is the slow one, at roughly 5–10 minutes.

`.github/workflows/publish-container.yml` publishes `ghcr.io/arkane-systems/h2s:<tag>` and `:latest` on tag push, but only if the tagged commit is on `master`. It first runs `ci.yml` as a reusable workflow and publishes only if that passes. Manual dispatch uses the latest tag.

Day-to-day work happens on feature branches off `dev`, one PR into `dev` per change, merged with merge commits (not squash, which would invalidate `.git-blame-ignore-revs`). `dev` merges to `master` for release.

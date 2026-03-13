# Copilot instructions for `h2s`

## Overview
This app is a single-page intranet dashboard for managing grouped links. It currently has a public dashboard view plus lightweight admin pages for editing categories, links, and dashboard settings.

## Big picture
- This is a single ASP.NET Core Razor Pages app targeting `.NET 10` (`h2s/h2s.csproj`).
- Persistence is EF Core + SQLite through one DbContext: `h2s/Data/DashboardContext.cs`.
- The app auto-applies EF Core migrations on startup in `h2s/Program.cs`.
- Memory caching is enabled and currently used by the admin editor for icon existence checks.
- Core domain model:
  - `Category` (`Id`, `Name`, `IsAdminCategory`) in `h2s/Models/Category.cs`
  - `Link` (`Id`, `CategoryId`, `Label`, `Description`, `IconName`, `Url`) in `h2s/Models/Link.cs`
  - `DashboardSettings` (`Id`, `Title`, `Motto`, `LocalDomains`, `ColorMode`) in `h2s/Models/DashboardSettings.cs` — singleton row, always `Id = 1`
  - `ColorMode` enum (`Auto`, `Light`, `Dark`) in `h2s/Models/ColorMode.cs`
- `DashboardContext` exposes `Categories`, `Links`, and `DashboardSettings`. It enforces the singleton settings row with a check constraint (`Id = 1`).
- Data flow:
  1. Request hits Razor PageModel (`Pages/**/*.cshtml.cs`)
  2. Settings access goes through `DashboardSettingsService` (`h2s/Services/DashboardSettingsService.cs`)
  3. Category/link management goes directly through `DashboardContext`
  4. Views render with Razor Pages, tag helpers, and small page-specific JavaScript where needed

## App structure and key patterns
- Public dashboard is `Pages/Index.cshtml(.cs)`.
  - It loads categories with links via EF Core.
  - Categories are ordered by `IsAdminCategory`, then `Name`.
  - Links are rendered sorted by `Label` in the page.
  - External-link badges are driven by `DashboardSettings.LocalDomains`.
- Shared layout is `Pages/Shared/_Layout.cshtml`.
  - It injects `DashboardSettingsService` directly to render title, motto, and theme state.
  - It contains the navbar links to the editor and settings pages.
  - It owns the inline theme bootstrap script and the Trianglify background rendering script.
- Admin section lives under `Pages/Admin/` and currently contains:
  - `Settings.cshtml(.cs)` — edits `Title`, `Motto`, `LocalDomains`, and `ColorMode`
  - `Editor.cshtml(.cs)` — manages categories and links with JSON page handlers plus client-side JS
  - `ToggleColorMode.cshtml.cs` — POST endpoint that cycles `ColorMode` and returns JSON
- `Editor.cshtml` is not scaffold-style CRUD.
  - The page initially renders server-side data.
  - `wwwroot/js/editor.js` drives add/edit/delete operations with `fetch`.
  - The PageModel exposes JSON handlers like `OnGetCategoriesAsync`, `OnPostCreateCategoryAsync`, `OnPostUpdateLinkAsync`, etc.
- `DashboardSettingsService` encapsulates retrieval and update of the singleton settings record.
  - If the row does not exist, it creates `Id = 1` with defaults: `Title = "Dashboard"`, `Motto = ""`, `LocalDomains = ""`, `ColorMode = Auto`.
- `Link` contains shared helper logic for icon handling.
  - `NormalizeIconName()` slugifies icon names.
  - `BuildIconUrl()` / `GetIconUrl()` build URLs against the selfh.st icons CDN.
- The admin editor probes icon availability remotely and caches results using `IMemoryCache`; keep that behavior if extending icon suggestion.
- Pipeline in `Program.cs` uses `.MapStaticAssets()` and `.MapRazorPages().WithStaticAssets()`; preserve this when editing startup.

## Configuration and environment behavior
- Connection string key is `ConnectionStrings:h2s`.
- Development DB path is absolute: `C:/Working/h2sdata/h2s.db` (`h2s/appsettings.Development.json`).
- Production DB path is `/app/data/h2s.db` (`h2s/appsettings.Production.json`).
- The production container is built from `h2s/Dockerfile` and expects the database volume mounted at `/app/data`.
- If adding new environments, keep the same connection string key and SQLite provider.

## Developer workflows
- Run from repo root: `dotnet run --project h2s/h2s.csproj`
- Build: `dotnet build h2s/h2s.csproj`
- Apply migrations: `dotnet ef database update --project h2s/h2s.csproj --startup-project h2s/h2s.csproj`
- Add migration: `dotnet ef migrations add <Name> --project h2s/h2s.csproj --startup-project h2s/h2s.csproj --output-dir Migrations`
- Launch profiles/ports are in `h2s/Properties/launchSettings.json`.
- There is currently no test project in this workspace; validate changes with build + manual page checks.
- Container publish automation lives in `.github/workflows/publish-container.yml`:
  - Triggered by tag pushes and manual runs.
  - For tag pushes, publish only occurs when the tagged commit is on `master`.
  - For manual runs, the workflow publishes using the latest git tag.
  - Images are pushed to GitHub Container Registry (`ghcr.io/<owner>/<repo>`) with both `<tag>` and `latest` tags.

## Code-change guidance for agents
- Prefer Razor Pages patterns already used here; do not introduce MVC controllers or Blazor unless explicitly requested.
- Keep edits minimal and local.
- For settings changes, prefer using `DashboardSettingsService` rather than duplicating singleton-record logic.
- For category/link changes, keep using `DashboardContext` directly in the relevant PageModel unless there is a clear need for a new service.
- When changing data shape, update models + `DashboardContext` + generate EF migration together.
- If you add new application-level services, place them in `h2s/Services/` and register them as scoped in `Program.cs` unless a different lifetime is clearly required.
- Preserve existing namespace/folder conventions (`h2s.Pages.Admin`, `h2s.Data`, `h2s.Models`, `h2s.Services`).
- Reuse existing frontend assets instead of introducing new frameworks.
  - Site-wide styling lives in `wwwroot/css/site.css`.
  - Admin editor behavior lives in `wwwroot/js/editor.js`.
- When changing editor behavior, update both the Razor markup and the corresponding JSON handlers / JavaScript contract.
- When adding function-level comments in C# code, use the standard .NET XML documentation comment format where appropriate.

## Dark mode / color mode

The app supports three color modes (stored in `DashboardSettings.ColorMode`):
- `Auto` (0) – follows the OS `prefers-color-scheme` media query.
- `Light` (1) – always light.
- `Dark` (2) – always dark.

Bootstrap 5.3's `data-bs-theme` attribute is set on `<html>` by an inline script in `<head>` before CSS loads, so there is no flash of incorrect theme. `_Layout.cshtml` also:
- renders the navbar theme-toggle button,
- posts to `/Admin/ToggleColorMode`,
- updates `data-bs-theme` client-side after toggles, and
- re-renders the Trianglify background when the effective theme changes.

### How to add dark-mode support to a new page element

1. **Prefer Bootstrap utility classes** — most Bootstrap components (`bg-body`, `bg-body-tertiary`, `text-body`, `text-muted`, `border`, etc.) automatically adapt when `data-bs-theme` changes.
2. **For custom CSS colours** — use Bootstrap CSS variables (`var(--bs-body-bg)`, `var(--bs-border-color)`, `var(--bs-link-color)`, `rgba(var(--bs-body-bg-rgb), ...)`, etc.) instead of hard-coded colours when the value should vary by theme.
3. **For theme-specific overrides** — scope rules with `[data-bs-theme="dark"]` / `[data-bs-theme="light"]` selectors.
4. **For JS-driven visuals** — read `document.documentElement.getAttribute('data-bs-theme')` and re-render when the theme changes. The Trianglify background in `_Layout.cshtml` is the reference implementation.
5. **For translucent cards and dashboard surfaces** — follow the existing styles in `wwwroot/css/site.css` (`category-box`, `link-block`, `home-search-form`) so the look stays consistent across themes.

## Dashboard-specific UI behavior
- Categories marked `IsAdminCategory = true` render after normal categories on the home page, separated by a grid break.
- Links can show:
  - a selfh.st icon when `IconName` is set,
  - a fallback dot when no icon is configured,
  - an `External` badge when the URL host does not match `LocalDomains`.
- The home page includes a Bing search box at the top; keep additions visually compatible with the existing card/grid layout.


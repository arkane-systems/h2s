# Copilot instructions for `h2s`

## Overview
This app is intended to be a single-page dashboard for an intranet, beginning with simple management of categories and
links, and growing to include more complex features over time.

## Big picture
- This is a single ASP.NET Core Razor Pages app targeting `.NET 10` (`h2s/h2s.csproj`).
- Persistence is EF Core + SQLite through one DbContext: `h2s/Data/DashboardContext.cs`.
- Core domain is a simple dashboard model:
  - `Category` (`Id`, `Name`, `SortOrder`) in `h2s/Models/Category.cs`
  - `Link` (`CategoryId`, `Label`, `Url`, `SortOrder`) in `h2s/Models/Link.cs`
  - `DashboardSettings` (`Id`, `Title`, `Motto`) in `h2s/Models/DashboardSettings.cs` — singleton row, always `Id = 1`
- Data flow:
  1. Request hits Razor PageModel (`Pages/**/*.cshtml.cs`)
  2. For settings, PageModel calls `DashboardSettingsService` (`h2s/Services/DashboardSettingsService.cs`)
  3. For categories/links, PageModel queries/updates `DashboardContext` directly
  4. Views render via strongly typed models and tag helpers (`Pages/**/*.cshtml`)

## App structure and key patterns
- Public dashboard is `Pages/Index.cshtml(.cs)`; it loads categories with links, sorted by `SortOrder`.
- Admin section lives under `Pages/Admin/`. Currently it contains:
  - `Settings.cshtml(.cs)` — edit the dashboard `Title` and `Motto` via `DashboardSettingsService`
- Admin pages are scaffold-style Razor Pages: `OnGetAsync`/`OnPostAsync`, `[BindProperty]`, redirect back to the same page after writes.
- `DashboardSettingsService` (`h2s/Services/DashboardSettingsService.cs`) encapsulates retrieval and update of the singleton `DashboardSettings` record; it auto-creates the record with defaults (`Title = "Dashboard"`, `Motto = ""`) if it does not exist.
- Pipeline in `Program.cs` uses `.MapStaticAssets()` and `.MapRazorPages().WithStaticAssets()`; keep this when editing startup.

## Configuration and environment behavior
- Connection string key is `ConnectionStrings:h2s`.
- Development DB path is absolute: `C:/Working/h2sdata/h2s.db` (`h2s/appsettings.Development.json`).
- Production DB path is `/app/data/h2s.db` (`h2s/appSettings.Production.json`).
- If adding new environments, keep the same connection string key and SQLite provider.

## Developer workflows
- Run from repo root: `dotnet run --project h2s/h2s.csproj`
- Build: `dotnet build h2s/h2s.csproj`
- Apply migrations: `dotnet ef database update --project h2s/h2s.csproj --startup-project h2s/h2s.csproj`
- Add migration: `dotnet ef migrations add <Name> --project h2s/h2s.csproj --startup-project h2s/h2s.csproj --output-dir Migrations`
- Launch profiles/ports are in `h2s/Properties/launchSettings.json`.
- There is currently no test project in this workspace; validate changes with build + manual page checks.
- In production, the app will be hosted in a container, built using the `h2s/Dockerfile`. The container expects the database file to be mounted at `/app/data/h2s.db`.

## Code-change guidance for agents
- Prefer Razor Pages patterns already used here (not MVC controllers or Blazor components).
- Keep edits minimal and local; this codebase currently favors straightforward PageModel + EF queries.
- When changing data shape, update models + `DashboardContext` + generate EF migration together.
- When adding new application-level services, place them in `h2s/Services/` and register them as scoped in `Program.cs`.
- Preserve existing namespace/folder conventions (`h2s.Pages.Admin.<Area>`, `h2s.Data`, `h2s.Models`, `h2s.Services`).

## Dark mode / color mode

The app supports three color modes (stored in `DashboardSettings.ColorMode`):
- `Auto` (0) – follows the OS `prefers-color-scheme` media query.
- `Light` (1) – always light.
- `Dark` (2) – always dark.

Bootstrap 5.3's `data-bs-theme` attribute is set on `<html>` by an inline script in `<head>` (before any CSS loads) so there is no flash of unstyled content. The JS in `_Layout.cshtml` then listens for system theme changes when the mode is `Auto`.

### How to add dark-mode support to a new page element

1. **Prefer Bootstrap utility classes** — most Bootstrap components (`bg-body`, `bg-body-tertiary`, `text-body`, `text-muted`, `border`, etc.) automatically invert when `data-bs-theme` changes. Use those instead of hard-coded colours wherever possible.
2. **For custom CSS colours** — use Bootstrap CSS custom properties (e.g. `var(--bs-body-bg)`, `var(--bs-border-color)`, `var(--bs-link-color)`) which Bootstrap re-defines per theme. Avoid hard-coded hex values for anything that should change between modes.
3. **For theme-specific overrides** — scope rules with the attribute selector, e.g.:
   ```css
   [data-bs-theme="dark"] .my-element { background: #1a1a2e; }
   [data-bs-theme="light"] .my-element { background: #f8f9fa; }
   ```
4. **For JS-driven visuals** (like the Trianglify background) — read the current theme with `document.documentElement.getAttribute('data-bs-theme')` and branch on `'dark'` vs `'light'`. Re-render whenever the theme changes by hooking into `renderPattern()` (or an equivalent callback already called after each toggle).


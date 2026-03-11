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
- Data flow:
  1. Request hits Razor PageModel (`Pages/**/*.cshtml.cs`)
  2. PageModel queries/updates `DashboardContext`
  3. Views render via strongly typed models and tag helpers (`Pages/**/*.cshtml`)

## App structure and key patterns
- Public dashboard is `Pages/Index.cshtml(.cs)`; it loads categories with links, sorted by `SortOrder`.
- Admin CRUD lives under `Pages/Admin/Categories/*` and `Pages/Admin/Links/*`. These pages are intended for use in the development stage and will be replaced by a more robust admin UI in the future, but they should be kept functional and up-to-date for now.
- Admin pages are scaffold-style Razor Pages: `OnGetAsync`/`OnPostAsync`, `[BindProperty]`, redirect back to `./Index` after writes.
- `Link` pages populate category dropdowns via `ViewData["CategoryId"]` + `SelectList` (see `Pages/Admin/Links/Create.cshtml.cs`).
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
- Preserve existing namespace/folder conventions (`h2s.Pages.Admin.<Area>`, `h2s.Data`, `h2s.Models`).

using h2s.Authorization;
using h2s.Data;
using h2s.Data.Seeding;
using h2s.Integrations;
using h2s.Integrations.JsonApi;
using h2s.Integrations.UptimeKuma;
using h2s.Panels;
using h2s.Services;
using h2s.Widgets;
using h2s.Widgets.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

// The seed verb's options are not configuration keys, so keep them away from the command-line configuration provider.
bool                  isSeedCommand = SeedCommand.IsInvoked (args);
WebApplicationBuilder builder       = WebApplication.CreateBuilder (isSeedCommand ? [] : args);

// Keep the seed command's output to its own messages rather than every EF Core SQL statement.
if (isSeedCommand)
{
  builder.Logging.AddFilter (category: "Microsoft.EntityFrameworkCore", level: LogLevel.Warning);
}

// Resolve a relative SQLite data source against the content root, so the database location does not depend on the
// working directory (development uses h2s/App_Data/h2s.db; production configures an absolute /app/data/h2s.db).
string configuredConnectionString = builder.Configuration.GetConnectionString ("h2s") ??
                                    throw new InvalidOperationException ("Connection string 'h2s' is not configured.");
string connectionString = SqliteConnectionStrings.ResolveDataSource (connectionString: configuredConnectionString,
                                                                     contentRootPath: builder.Environment.ContentRootPath);
SqliteConnectionStrings.EnsureDirectoryExists (connectionString);

// Register Razor Pages and application services used by the dashboard and admin UI.
builder.Services.AddRazorPages (options => options.Conventions.AuthorizeFolder (folderPath: "/Admin",
                                                                                policy: AdminRequirement.PolicyName))
       .AddMvcOptions (options =>
                       {
                         // Form-bound models must state [Required] explicitly. Otherwise every non-nullable string
                         // is implicitly required, and optional text fields such as the motto can never be cleared.
                         options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
                       });
builder.Services.AddMemoryCache ();
builder.Services.AddHttpClient ();
builder.Services.AddDbContext<DashboardContext> (options => options.UseSqlite (connectionString));
builder.Services.AddScoped<DashboardSettingsService> ();
builder.Services.AddScoped<IconCatalog> ();
builder.Services.AddScoped<UrlProbe> ();
builder.Services.AddScoped<PageMetadataReader> ();
builder.Services.AddScoped<UptimeKumaClient> ();
builder.Services.AddScoped<JsonApiClient> ();
builder.Services.AddScoped<DashboardBackupService> ();

// Integrations are singletons; the catalog lists them, and ConnectionService reads and saves their connections, whose
// secrets ConnectionSecrets encrypts.
builder.Services.AddSingleton<IIntegration, UptimeKumaIntegration> ();
builder.Services.AddSingleton<IIntegration, JsonApiIntegration> ();
builder.Services.AddSingleton<IntegrationCatalog> ();
builder.Services.AddSingleton<ConnectionSecrets> ();
builder.Services.AddScoped<ConnectionService> ();
builder.Services.AddScoped<ChoiceListReader> ();

// Panel kinds are singletons; the catalog lists them, and PanelService builds each panel's content per request.
builder.Services.AddSingleton<IPanelKind, StatusBarPanelKind> ();
builder.Services.AddSingleton<IPanelKind, SearchPanelKind> ();
builder.Services.AddSingleton<PanelCatalog> ();
builder.Services.AddScoped<PanelService> ();

// Widget kinds come from the integrations. The fetcher, a singleton, keeps results and shares reads in flight; the
// service loads links' widgets per request.
builder.Services.AddSingleton<WidgetCatalog> ();
builder.Services.AddSingleton<WidgetFetcher> ();
builder.Services.AddScoped<WidgetService> ();

// Named clients for outbound HTTP. The accept-any-certificate client exists only so UrlProbe can tell an untrusted
// certificate from an unreachable server; never use it to fetch content.
builder.Services.AddHttpClient (name: IconCatalog.ClientName, configureClient: client => client.Timeout = IconCatalog.Timeout);
builder.Services.AddHttpClient (name: UptimeKumaClient.ClientName,
                                configureClient: client => client.Timeout = UptimeKumaClient.Timeout);

// JSON API connections send credentials, so this client never follows a redirect, which could carry them to another host.
builder.Services.AddHttpClient (name: JsonApiClient.ClientName, configureClient: client => client.Timeout = JsonApiClient.Timeout)
       .ConfigurePrimaryHttpMessageHandler (() => new HttpClientHandler { AllowAutoRedirect = false });

// Fetches the pages people add links to, for suggestions. It names itself, since some sites refuse anonymous clients,
// and follows only a few redirects.
builder.Services.AddHttpClient (name: PageMetadataReader.ClientName,
                                configureClient: client =>
                                                 {
                                                   client.Timeout = PageMetadataReader.Timeout;
                                                   client.DefaultRequestHeaders.UserAgent
                                                         .ParseAdd ("h2s (+https://github.com/arkane-systems/h2s)");
                                                 })
       .ConfigurePrimaryHttpMessageHandler (() => new HttpClientHandler { MaxAutomaticRedirections = 5 });
builder.Services.AddHttpClient (name: UrlProbe.ClientName, configureClient: client => client.Timeout = UrlProbe.Timeout);
builder.Services.AddHttpClient (name: UrlProbe.AcceptAnyCertificateClientName,
                                configureClient: client => client.Timeout = UrlProbe.Timeout)
       .ConfigurePrimaryHttpMessageHandler (() => new HttpClientHandler
                                                  {
                                                    ServerCertificateCustomValidationCallback =
                                                      HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                                                  });

// The admin pages require the Admin policy. Its handler currently allows everyone; see AllowAllAdminHandler.
builder.Services.AddAuthorizationBuilder ()
       .AddPolicy (name: AdminRequirement.PolicyName,
                   configurePolicy: policy => policy.AddRequirements (new AdminRequirement ()));
builder.Services.AddSingleton<IAuthorizationHandler, AllowAllAdminHandler> ();

// Keep Data Protection keys beside the database, so antiforgery tokens and connection secrets survive restarts.
IDataProtectionBuilder dataProtection = builder.Services.AddDataProtection ()
                                               .SetApplicationName ("h2s");
string? dataDirectory = SqliteConnectionStrings.GetDataDirectory (connectionString);

if (dataDirectory != null)
{
  dataProtection.PersistKeysToFileSystem (new DirectoryInfo (Path.Combine (path1: dataDirectory, path2: "keys")));
}

// The app runs behind a reverse proxy (Traefik) and is reachable only through it, so trust forwarded headers from any
// address rather than only loopback.
builder.Services.Configure<ForwardedHeadersOptions> (options =>
                                                     {
                                                       options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                                                                  ForwardedHeaders.XForwardedProto;
                                                       options.KnownIPNetworks.Clear ();
                                                       options.KnownProxies.Clear ();
                                                     });

builder.Services.AddHealthChecks ()
       .AddDbContextCheck<DashboardContext> ();

WebApplication app = builder.Build ();

if (isSeedCommand)
{
  return await SeedCommand.RunAsync (app: app, args: args);
}

// Apply any pending EF Core migrations during startup so the SQLite schema stays current.
using (IServiceScope scope = app.Services.CreateScope ())
{
  var db = scope.ServiceProvider.GetRequiredService<DashboardContext> ();
  db.Database.Migrate ();
}

// Configure the HTTP request pipeline. Forwarded headers come first, so everything after sees the original scheme and
// client address.
app.UseForwardedHeaders ();

if (!app.Environment.IsDevelopment ())
{
  _ = app.UseExceptionHandler ("/Error");
}

app.UseRouting ();

app.UseAuthorization ();

// Preserve static asset mapping used by the .NET 10 Razor Pages static asset pipeline.
app.MapStaticAssets ();
app.MapRazorPages ()
   .WithStaticAssets ();

// Liveness and readiness probe: succeeds when the database is reachable.
app.MapHealthChecks ("/healthz");

await app.RunAsync ();

return 0;

/// <summary>
///   Entry point type for the dashboard application, exposed so integration tests can host it with
///   <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;

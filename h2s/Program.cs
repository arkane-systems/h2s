using h2s.Data;
using h2s.Data.Seeding;
using h2s.Services;
using Microsoft.EntityFrameworkCore;

// The seed verb's options are not configuration keys, so keep them away from the command-line configuration provider.
bool                  isSeedCommand = SeedCommand.IsInvoked (args);
WebApplicationBuilder builder       = WebApplication.CreateBuilder (isSeedCommand ? [] : args);

// Keep the seed command's output to its own messages rather than every EF Core SQL statement.
if (isSeedCommand)
  builder.Logging.AddFilter (category: "Microsoft.EntityFrameworkCore", level: LogLevel.Warning);

// Resolve a relative SQLite data source against the content root, so the database location does not depend on the
// working directory (development uses h2s/App_Data/h2s.db; production configures an absolute /app/data/h2s.db).
string configuredConnectionString = builder.Configuration.GetConnectionString ("h2s") ??
                                    throw new InvalidOperationException ("Connection string 'h2s' is not configured.");
string connectionString = SqliteConnectionStrings.ResolveDataSource (connectionString: configuredConnectionString,
                                                                     contentRootPath: builder.Environment.ContentRootPath);
SqliteConnectionStrings.EnsureDirectoryExists (connectionString);

// Register Razor Pages and application services used by the dashboard and admin UI.
builder.Services.AddRazorPages ()
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

WebApplication app = builder.Build ();

if (isSeedCommand)
  return await SeedCommand.RunAsync (app: app, args: args);

// Apply any pending EF Core migrations during startup so the SQLite schema stays current.
using (IServiceScope scope = app.Services.CreateScope ())
{
  var db = scope.ServiceProvider.GetRequiredService<DashboardContext> ();
  db.Database.Migrate ();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment ())
  _ = app.UseExceptionHandler ("/Error");

app.UseRouting ();

app.UseAuthorization ();

// Preserve static asset mapping used by the .NET 10 Razor Pages static asset pipeline.
app.MapStaticAssets ();
app.MapRazorPages ()
   .WithStaticAssets ();

await app.RunAsync ();

return 0;

/// <summary>
///   Entry point type for the dashboard application, exposed so integration tests can host it with
///   <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;

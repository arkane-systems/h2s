using h2s.Data;
using h2s.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register Razor Pages and application services used by the dashboard and admin UI.
builder.Services.AddRazorPages ();
builder.Services.AddMemoryCache ();
builder.Services.AddDbContext<DashboardContext> (options =>
    options.UseSqlite (builder.Configuration.GetConnectionString ("h2s")));
builder.Services.AddScoped<DashboardSettingsService> ();

var app = builder.Build();

// Apply any pending EF Core migrations during startup so the SQLite schema stays current.
using (var scope = app.Services.CreateScope ())
{
  var db = scope.ServiceProvider.GetRequiredService<DashboardContext> ();
  db.Database.Migrate ();
}

// Configure the HTTP request pipeline.
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

app.Run ();

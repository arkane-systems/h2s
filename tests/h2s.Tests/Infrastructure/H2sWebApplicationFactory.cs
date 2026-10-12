using h2s.Data;
using h2s.Data.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace h2s.Tests.Infrastructure;

/// <summary>
/// Hosts the dashboard in-process over a private in-memory SQLite database seeded with <see cref="DevDataSeeder"/>
/// sample data. Create one per test that changes data, so tests stay independent.
/// </summary>
public sealed class H2sWebApplicationFactory : WebApplicationFactory<Program>
{
  private readonly SqliteConnection connection = new ("Data Source=:memory:");

  /// <summary>
  /// Initializes a new instance of the <see cref="H2sWebApplicationFactory"/> class.
  /// </summary>
  public H2sWebApplicationFactory () => this.connection.Open ();

  /// <summary>
  /// Creates a database context over the factory's database for arranging or asserting on data directly.
  /// </summary>
  /// <returns>A service scope and the context resolved from it; dispose the scope when done.</returns>
  public (IServiceScope Scope, DashboardContext Context) CreateContext ()
  {
    var scope = this.Services.CreateScope ();

    return (scope, scope.ServiceProvider.GetRequiredService<DashboardContext> ());
  }

  /// <inheritdoc />
  public override async ValueTask DisposeAsync ()
  {
    await base.DisposeAsync ();
    await this.connection.DisposeAsync ();
  }

  /// <inheritdoc />
  protected override void ConfigureWebHost (IWebHostBuilder builder)
  {
    builder.UseEnvironment ("Development");
    builder.ConfigureLogging (logging => logging.AddFilter ("Microsoft.EntityFrameworkCore", LogLevel.Warning));
    builder.ConfigureTestServices (services =>
                                   {
                                     // Replace the configured file database with the shared in-memory connection.
                                     services.RemoveAll<DbContextOptions<DashboardContext>> ();
                                     services.RemoveAll<IDbContextOptionsConfiguration<DashboardContext>> ();
                                     services.AddDbContext<DashboardContext> (options => options.UseSqlite (this.connection));
                                   });
  }

  /// <inheritdoc />
  protected override IHost CreateHost (IHostBuilder builder)
  {
    // Program applies migrations during startup, so the schema exists once the host is created.
    var host = base.CreateHost (builder);

    using var scope = host.Services.CreateScope ();
    DevDataSeeder.SeedAsync (scope.ServiceProvider.GetRequiredService<DashboardContext> ()).GetAwaiter ().GetResult ();

    return host;
  }

  /// <inheritdoc />
  protected override void Dispose (bool disposing)
  {
    base.Dispose (disposing);

    if (disposing)
    {
      this.connection.Dispose ();
    }
  }
}

using System.Net;
using System.Text;
using h2s.Data;
using h2s.Data.Seeding;
using h2s.Services;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
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
///   Hosts the dashboard in-process over a private in-memory SQLite database seeded with <see cref="DevDataSeeder" />
///   sample data. Create one per test that changes data, so tests stay independent.
/// </summary>
public sealed class DashboardApplicationFactory : WebApplicationFactory<Program>
{
  /// <summary>
  ///   The selfh.st icon index the stubbed CDN serves: a few icons the sample data and tests use.
  /// </summary>
  public const string IconIndexJson = """
                                      [
                                        { "Name": "Jellyfin", "Reference": "jellyfin" },
                                        { "Name": "Home Assistant", "Reference": "home-assistant" },
                                        { "Name": "GitHub", "Reference": "github" },
                                        { "Name": "Navidrome", "Reference": "navidrome", "Tags": "Music" }
                                      ]
                                      """;

  private readonly SqliteConnection connection = new ("Data Source=:memory:");

  /// <summary>
  ///   Initializes a new instance of the <see cref="DashboardApplicationFactory" /> class.
  /// </summary>
  public DashboardApplicationFactory () => this.connection.Open ();

  /// <summary>
  ///   Gets or sets how the stubbed network answers the icon catalog, the page metadata reader, the Uptime Kuma client and
  ///   the URL probe. By default it
  ///   serves
  ///   <see cref="IconIndexJson" /> as the icon index and 404 for everything else. Set it before the first request.
  /// </summary>
  public Func<HttpRequestMessage, HttpResponseMessage> Outbound { get; set; } = DashboardApplicationFactory.ServeIconIndex;

  /// <summary>
  ///   Gets or sets a last change to the app's services, such as registering a test-only integration. Set it before the
  ///   first request.
  /// </summary>
  public Action<IServiceCollection>? ConfigureServices { get; set; }

  /// <summary>
  ///   Answers with the stub icon index for its URL, and 404 for any other.
  /// </summary>
  /// <param name="request">The outbound request.</param>
  /// <returns>The response.</returns>
  public static HttpResponseMessage ServeIconIndex (HttpRequestMessage request)
    => request.RequestUri == new Uri (IconCatalog.IndexUrl)
         ? new HttpResponseMessage (HttpStatusCode.OK)
           {
             Content = new StringContent (content: DashboardApplicationFactory.IconIndexJson,
                                          encoding: Encoding.UTF8,
                                          mediaType: "application/json"),
           }
         : new HttpResponseMessage (HttpStatusCode.NotFound);

  /// <summary>
  ///   Creates a database context over the factory's database for arranging or asserting on data directly.
  /// </summary>
  /// <returns>A service scope and the context resolved from it; dispose the scope when done.</returns>
  public (IServiceScope Scope, DashboardContext Context) CreateContext ()
  {
    IServiceScope scope = this.Services.CreateScope ();

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
    builder.ConfigureLogging (logging => logging.AddFilter (category: "Microsoft.EntityFrameworkCore", level: LogLevel.Warning));
    builder.ConfigureTestServices (services =>
                                   {
                                     // Replace the configured file database with the shared in-memory connection.
                                     services.RemoveAll<DbContextOptions<DashboardContext>> ();
                                     services.RemoveAll<IDbContextOptionsConfiguration<DashboardContext>> ();
                                     services.AddDbContext<DashboardContext> (options => options.UseSqlite (this.connection));

                                     // Keep every outbound client off the network.
                                     foreach (string name in new[]
                                                             {
                                                               IconCatalog.ClientName,
                                                               PageMetadataReader.ClientName,
                                                               UptimeKumaClient.ClientName,
                                                               UrlProbe.ClientName,
                                                               UrlProbe.AcceptAnyCertificateClientName,
                                                             })
                                     {
                                       services.AddHttpClient (name)
                                               .ConfigurePrimaryHttpMessageHandler (() => new StubHttpMessageHandler (request
                                                                                                                        => this
                                                                                                                         .Outbound (request)));
                                     }

                                     // Keep Data Protection keys in memory rather than in the source tree's App_Data.
                                     services.Configure<KeyManagementOptions> (options => options.XmlRepository =
                                                                                            new InMemoryXmlRepository ());

                                     this.ConfigureServices?.Invoke (services);
                                   });
  }

  /// <inheritdoc />
  protected override IHost CreateHost (IHostBuilder builder)
  {
    // Program applies migrations during startup, so the schema exists once the host is created.
    IHost host = base.CreateHost (builder);

    using IServiceScope scope = host.Services.CreateScope ();
    DevDataSeeder.SeedAsync (scope.ServiceProvider.GetRequiredService<DashboardContext> ()).GetAwaiter ().GetResult ();

    return host;
  }

  /// <inheritdoc />
  protected override void Dispose (bool disposing)
  {
    base.Dispose (disposing);

    if (disposing)
      this.connection.Dispose ();
  }
}

using System.Text.Json;
using h2s.Data;
using h2s.Integrations;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Widgets.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Infrastructure;

/// <summary>
///   Arranges and reads connections in a test database.
/// </summary>
public static class TestConnections
{
  /// <summary>
  ///   The Uptime Kuma server the tests' connections point at.
  /// </summary>
  public const string KumaServerUrl = "https://status.home.arpa";

  /// <summary>
  ///   Creates an Uptime Kuma connection, as Settings saves it.
  /// </summary>
  /// <param name="statusPageSlug">The status page slug; blank for none.</param>
  /// <param name="defaultDuration">The default uptime badge period, in hours.</param>
  /// <param name="serverUrl">The server URL.</param>
  /// <returns>The connection, not yet added to a context.</returns>
  public static Connection
    UptimeKuma (string statusPageSlug = "", int defaultDuration = 24, string serverUrl = TestConnections.KumaServerUrl)
    => new ()
       {
         IntegrationKey = UptimeKumaIntegration.IntegrationKey,
         Name           = "Uptime Kuma",
         OptionsJson = JsonSerializer.Serialize (new Dictionary<string, object>
                                                 {
                                                   [UptimeKumaIntegration.ServerUrlOption]       = serverUrl,
                                                   [UptimeKumaIntegration.StatusPageSlugOption]  = statusPageSlug,
                                                   [UptimeKumaIntegration.DefaultDurationOption] = defaultDuration,
                                                 }),
       };

  /// <summary>
  ///   Creates a connection service with the built-in integrations, for unit tests that build their services by hand.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <param name="cache">The cache the service keeps the connections in.</param>
  /// <returns>The service.</returns>
  public static ConnectionService CreateService (DashboardContext context, IMemoryCache cache)
    => new (context: context,
            integrations: new IntegrationCatalog ([new UptimeKumaIntegration ()]),
            secrets: new ConnectionSecrets (new EphemeralDataProtectionProvider ()),
            cache: cache);

  /// <summary>
  ///   Adds an Uptime Kuma connection to an application's database. Call it before the first request, since the app caches
  ///   connections.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="statusPageSlug">The status page slug; blank for none.</param>
  /// <param name="defaultDuration">The default uptime badge period, in hours.</param>
  public static async Task AddUptimeKumaAsync (this DashboardApplicationFactory factory,
                                               string                           statusPageSlug  = "",
                                               int                              defaultDuration = 24)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      context.Connections.Add (TestConnections.UptimeKuma (statusPageSlug: statusPageSlug, defaultDuration: defaultDuration));
      await context.SaveChangesAsync (TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Reads the Uptime Kuma connection's settings from a database.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <returns>The settings, or <c>null</c> when there's no Uptime Kuma connection.</returns>
  public static async Task<UptimeKumaSettings?> ReadUptimeKumaAsync (DashboardContext context)
  {
    Connection? connection = await context.Connections
                                          .AsNoTracking ()
                                          .SingleOrDefaultAsync (predicate: c => c.IntegrationKey ==
                                                                                 UptimeKumaIntegration.IntegrationKey,
                                                                 cancellationToken: TestContext.Current.CancellationToken);

    return connection == null
             ? null
             : UptimeKumaSettings.From (new OptionValues (schema: new UptimeKumaIntegration ().ConnectionOptions,
                                                          json: connection.OptionsJson));
  }

  /// <summary>
  ///   Reads the Uptime Kuma connection's settings from an application's database.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The settings, or <c>null</c> when there's no Uptime Kuma connection.</returns>
  public static async Task<UptimeKumaSettings?> ReadUptimeKumaAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
      return await TestConnections.ReadUptimeKumaAsync (context);
  }
}

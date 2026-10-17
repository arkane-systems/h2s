using h2s.Data;
using h2s.Models;
using h2s.Widgets.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Integrations;

/// <summary>
///   Reads and saves connections (<see cref="Connection" />), and reads their settings against their integration's
///   schema.
/// </summary>
/// <remarks>
///   The dashboard reads connections on every page (Uptime Kuma's badges need its server), so they are cached in memory
///   and the cache is cleared on save. Writes that bypass this service, such as the seeder, show up once the cache entry
///   expires; an import clears it through <see cref="ForgetCached" />.
/// </remarks>
public sealed class ConnectionService
{
  private const string CacheKey = "connections";

  private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes (5);

  private readonly IMemoryCache       cache;
  private readonly DashboardContext   context;
  private readonly IntegrationCatalog integrations;
  private readonly ConnectionSecrets  secrets;

  /// <summary>
  ///   Initializes a new instance of the <see cref="ConnectionService" /> class.
  /// </summary>
  /// <param name="context">The database context holding the connections.</param>
  /// <param name="integrations">The known integrations, whose schemas the settings are read against.</param>
  /// <param name="secrets">Decrypts the connections' secret settings.</param>
  /// <param name="cache">The cache that holds the connections between requests.</param>
  public ConnectionService (DashboardContext   context,
                            IntegrationCatalog integrations,
                            ConnectionSecrets  secrets,
                            IMemoryCache       cache)
  {
    this.context      = context;
    this.integrations = integrations;
    this.secrets      = secrets;
    this.cache        = cache;
  }

  /// <summary>
  ///   Gets every connection.
  /// </summary>
  /// <param name="cancellationToken">A token used to cancel the query.</param>
  /// <returns>Copies of the connections, which the caller may change, ordered by integration and name.</returns>
  public async Task<List<Connection>> GetConnectionsAsync (CancellationToken cancellationToken = default)
  {
    if (!this.cache.TryGetValue (key: ConnectionService.CacheKey, value: out List<Connection>? connections) ||
        (connections == null))
    {
      connections = await this.context.Connections
                              .AsNoTracking ()
                              .OrderBy (c => c.IntegrationKey)
                              .ThenBy (c => c.Name)
                              .ThenBy (c => c.Id)
                              .ToListAsync (cancellationToken);
      this.cache.Set (key: ConnectionService.CacheKey,
                      value: connections,
                      absoluteExpirationRelativeToNow: ConnectionService.CacheDuration);
    }

    return [.. connections.Select (c => c.Clone ())];
  }

  /// <summary>
  ///   Finds an integration's first connection, which is its only one when it allows no more.
  /// </summary>
  /// <param name="integrationKey">The integration's key.</param>
  /// <param name="cancellationToken">A token used to cancel the query.</param>
  /// <returns>A copy of the connection, or <c>null</c> when the integration has none.</returns>
  public async Task<Connection?> FindFirstAsync (string integrationKey, CancellationToken cancellationToken = default)
    => (await this.GetConnectionsAsync (cancellationToken)).Where (c => c.IntegrationKey == integrationKey)
                                                           .MinBy (c => c.Id);

  /// <summary>
  ///   Reads a connection's settings, secrets included, against its integration's schema.
  /// </summary>
  /// <param name="connection">The connection.</param>
  /// <returns>The settings, or <c>null</c> when this build of h2s doesn't know the connection's integration.</returns>
  public OptionValues? ReadOptions (Connection connection)
    => this.integrations.Find (connection.IntegrationKey) is { } integration
         ? new OptionValues (schema: integration.ConnectionOptions,
                             json: connection.OptionsJson,
                             secrets: this.secrets.Unprotect (connection.SecretsJson))
         : null;

  /// <summary>
  ///   Decrypts a connection's secret settings.
  /// </summary>
  /// <param name="connection">The connection.</param>
  /// <returns>The secrets, keyed by option name; empty when none are set or they can no longer be decrypted.</returns>
  public IReadOnlyDictionary<string, string> ReadSecrets (Connection connection) => this.secrets.Unprotect (connection.SecretsJson);

  /// <summary>
  ///   Saves an integration's only connection, creating it (named after the integration) if it doesn't exist yet.
  /// </summary>
  /// <param name="integration">The integration.</param>
  /// <param name="optionsJson">The connection's settings, already checked against the integration's schema.</param>
  /// <param name="secrets">
  ///   The connection's secret settings, keyed by option name, which replace those stored; <c>null</c> keeps them.
  /// </param>
  /// <param name="cancellationToken">A token used to cancel the save.</param>
  /// <returns>The saved connection.</returns>
  public async Task<Connection> SaveOnlyAsync (IIntegration                         integration,
                                               string                               optionsJson,
                                               IReadOnlyDictionary<string, string>? secrets           = null,
                                               CancellationToken                    cancellationToken = default)
  {
    Connection? connection = await this.context.Connections
                                       .Where (c => c.IntegrationKey == integration.Key)
                                       .OrderBy (c => c.Id)
                                       .FirstOrDefaultAsync (cancellationToken);

    if (connection == null)
    {
      connection = new Connection { IntegrationKey = integration.Key, Name = integration.DisplayName };
      this.context.Connections.Add (connection);
    }

    connection.OptionsJson = optionsJson;

    if (secrets != null)
    {
      connection.SecretsJson = this.secrets.Protect (secrets);
    }

    await this.context.SaveChangesAsync (cancellationToken);
    this.ForgetCached ();

    return connection.Clone ();
  }

  /// <summary>
  ///   Deletes every connection of an integration.
  /// </summary>
  /// <param name="integrationKey">The integration's key.</param>
  /// <param name="cancellationToken">A token used to cancel the delete.</param>
  public async Task DeleteAllAsync (string integrationKey, CancellationToken cancellationToken = default)
  {
    _ = await this.context.Connections
                  .Where (c => c.IntegrationKey == integrationKey)
                  .ExecuteDeleteAsync (cancellationToken);
    this.ForgetCached ();
  }

  /// <summary>
  ///   Clears the cached connections, after a write that didn't go through this service.
  /// </summary>
  public void ForgetCached () => this.cache.Remove (ConnectionService.CacheKey);
}

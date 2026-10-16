using h2s.Widgets.Abstractions;

namespace h2s.Integrations.UptimeKuma;

/// <summary>
///   The Uptime Kuma connection's settings. Uptime Kuma is on when there's a connection, which always has a server.
/// </summary>
/// <param name="ServerUrl">The server's base URL.</param>
/// <param name="StatusPageSlug">
///   The slug of the status page the status bar links to and the monitor picker lists, or <c>null</c> for none.
/// </param>
/// <param name="DefaultDuration">The period, in hours, of uptime badges that don't set their own.</param>
public sealed record UptimeKumaSettings (string ServerUrl, string? StatusPageSlug, int DefaultDuration)
{
  /// <summary>
  ///   Reads the settings from a connection's option values.
  /// </summary>
  /// <param name="options">The option values, read against <see cref="UptimeKumaIntegration" />'s schema.</param>
  /// <returns>The settings, or <c>null</c> when they have no usable server URL.</returns>
  public static UptimeKumaSettings? From (OptionValues? options)
  {
    string serverUrl = options?.GetText (UptimeKumaIntegration.ServerUrlOption).Trim () ?? "";

    if (serverUrl.Length == 0)
      return null;

    string slug = options!.GetText (UptimeKumaIntegration.StatusPageSlugOption).Trim ();

    return new UptimeKumaSettings (ServerUrl: serverUrl,
                                   StatusPageSlug: slug.Length == 0 ? null : slug,
                                   DefaultDuration: options.GetInteger (UptimeKumaIntegration.DefaultDurationOption) ??
                                                    UptimeKumaIntegration.DefaultDuration);
  }

  /// <summary>
  ///   Loads the Uptime Kuma connection's settings.
  /// </summary>
  /// <param name="connections">The service holding the connections.</param>
  /// <param name="cancellationToken">A token used to cancel the query.</param>
  /// <returns>The settings, or <c>null</c> when Uptime Kuma is off.</returns>
  public static async Task<UptimeKumaSettings?> LoadAsync (ConnectionService connections,
                                                           CancellationToken cancellationToken = default)
    => await connections.FindFirstAsync (integrationKey: UptimeKumaIntegration.IntegrationKey,
                                         cancellationToken: cancellationToken) is { } connection
         ? UptimeKumaSettings.From (connections.ReadOptions (connection))
         : null;
}

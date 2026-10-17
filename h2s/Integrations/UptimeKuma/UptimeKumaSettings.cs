using h2s.Widgets.Abstractions;

namespace h2s.Integrations.UptimeKuma;

/// <summary>
///   An Uptime Kuma connection's settings, as its widgets and connection test read them. A connection always has a server.
/// </summary>
/// <param name="ServerUrl">The server's base URL.</param>
/// <param name="DefaultUptimeWindow">
///   The uptime window (<see cref="UptimeKumaWindows" />) of uptime figures that don't set their own.
/// </param>
public sealed record UptimeKumaSettings (string ServerUrl, string DefaultUptimeWindow)
{
  /// <summary>
  ///   Gets the API key that reads Kuma's <c>/metrics</c>, or <c>null</c> when none is set (or it can no longer be
  ///   decrypted). It's left out of <see cref="ToString" />, so it never reaches a log.
  /// </summary>
  public string? ApiKey { get; init; }

  /// <summary>
  ///   Reads the settings from a connection's option values.
  /// </summary>
  /// <param name="options">The option values, read against <see cref="UptimeKumaIntegration" />'s schema.</param>
  /// <returns>The settings, or <c>null</c> when they have no usable server URL.</returns>
  public static UptimeKumaSettings? From (OptionValues? options)
  {
    string serverUrl = options?.GetText (UptimeKumaIntegration.ServerUrlOption).Trim () ?? "";

    if (serverUrl.Length == 0)
    {
      return null;
    }

    string window = options!.GetText (UptimeKumaIntegration.DefaultUptimeWindowOption);

    return new UptimeKumaSettings (ServerUrl: serverUrl,
                                   DefaultUptimeWindow: window.Length == 0 ? UptimeKumaWindows.OneDay : window)
           {
             ApiKey = options.GetSecret (UptimeKumaIntegration.ApiKeyOption),
           };
  }

  /// <inheritdoc />
  public override string ToString ()
    => $"UptimeKumaSettings {{ ServerUrl = {this.ServerUrl}, DefaultUptimeWindow = {this.DefaultUptimeWindow} }}";
}

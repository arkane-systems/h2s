using h2s.Models;

namespace h2s.Pages;

/// <summary>
///   Builds dashboard tiles (<see cref="LinkTile" />) for one set of dashboard settings. The dashboard uses it to render
///   its tiles, and the editor uses it to render previews and refreshed tiles that look the same.
/// </summary>
public sealed class LinkTileFactory
{
  private readonly HashSet<string> localDomains;

  private readonly string? uptimeKumaServerUrl;

  /// <summary>
  ///   Initializes a new instance of the <see cref="LinkTileFactory" /> class.
  /// </summary>
  /// <param name="settings">The dashboard settings that decide which links are external and where badges come from.</param>
  public LinkTileFactory (DashboardSettings settings)
  {
    this.localDomains        = LinkTileFactory.ParseLocalDomains (settings.LocalDomains);
    this.uptimeKumaServerUrl = settings.UptimeKumaServerUrl;
  }

  /// <summary>
  ///   Gets a value indicating whether Uptime Kuma integration is configured.
  /// </summary>
  public bool IsUptimeKumaConfigured => !string.IsNullOrWhiteSpace (this.uptimeKumaServerUrl);

  /// <summary>
  ///   Builds the view model for a link's tile.
  /// </summary>
  /// <param name="link">The link to show.</param>
  /// <returns>The tile, with its external flag and, when Uptime Kuma is configured and the link has a monitor, its status badge.</returns>
  public LinkTile Create (Link link)
    => new (Link: link,
            IsExternal: this.IsExternalLink (link.Url),
            StatusBadgeUrl: this.IsUptimeKumaConfigured && link.MonitorId.HasValue
                              ? this.BuildBadgeUrl (monitorId: link.MonitorId.Value, type: "status", label: "")
                              : null);

  /// <summary>
  ///   Builds the URL for an Uptime Kuma badge.
  /// </summary>
  /// <param name="monitorId">The monitor ID.</param>
  /// <param name="type">The badge type (status, uptime, ping, etc.).</param>
  /// <param name="duration">Optional duration in hours for time-based badges.</param>
  /// <param name="label">Optional override for badge label text. Use an empty string to hide the label.</param>
  /// <returns>The badge URL, or an empty string if server URL is not configured.</returns>
  public string BuildBadgeUrl (int monitorId, string type, int? duration = null, string? label = null)
  {
    if (string.IsNullOrWhiteSpace (this.uptimeKumaServerUrl))
      return "";

    var baseUrl = $"{this.uptimeKumaServerUrl.TrimEnd ('/')}/api/badge/{monitorId}/{type}";

    if (duration.HasValue)
      baseUrl += $"/{duration.Value}";

    var query = "style=flat";
    if (label != null)
      query += $"&label={Uri.EscapeDataString (label)}";

    return $"{baseUrl}?{query}";
  }

  /// <summary>
  ///   Determines whether a link target should be marked as external based on the configured local domains.
  /// </summary>
  /// <param name="url">The absolute URL to evaluate.</param>
  /// <returns><c>true</c> when the URL points to an external host; otherwise, <c>false</c>.</returns>
  public bool IsExternalLink (string url)
  {
    if (this.localDomains.Count == 0)
      return false;

    if (!Uri.TryCreate (uriString: url, uriKind: UriKind.Absolute, result: out Uri? uri) || string.IsNullOrWhiteSpace (uri.Host))
      return false;

    string host = uri.Host.ToLowerInvariant ();
    bool isInternal = this.localDomains.Any (domain => (host == domain) ||
                                                       host.EndsWith (value: $".{domain}",
                                                                      comparisonType: StringComparison.Ordinal));

    return !isInternal;
  }

  /// <summary>
  ///   Parses the configured local-domain list into a normalized set of unique host names.
  /// </summary>
  /// <param name="domains">The raw domain list from settings.</param>
  /// <returns>A case-insensitive set of normalized domains.</returns>
  private static HashSet<string> ParseLocalDomains (string domains)
  {
    var parsedDomains = new HashSet<string> (StringComparer.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace (domains))
      return parsedDomains;

    var separators = new[] { ',', ';', '\n', '\r', '\t', ' ' };

    foreach (string value in domains.Split (separator: separators,
                                            options: StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      string normalized = LinkTileFactory.NormalizeDomain (value);
      if (!string.IsNullOrWhiteSpace (normalized))
        _ = parsedDomains.Add (normalized);
    }

    return parsedDomains;
  }

  /// <summary>
  ///   Normalizes a configured domain or URL down to its lowercase host name.
  /// </summary>
  /// <param name="domain">The raw domain or URL value.</param>
  /// <returns>The normalized host name, or an empty string when the input is blank.</returns>
  private static string NormalizeDomain (string domain)
  {
    if (string.IsNullOrWhiteSpace (domain))
      return "";

    if (Uri.TryCreate (uriString: domain.Trim (), uriKind: UriKind.Absolute, result: out Uri? parsedUri) &&
        !string.IsNullOrWhiteSpace (parsedUri.Host))
      return parsedUri.Host.Trim ().TrimStart ('.').ToLowerInvariant ();

    return domain.Trim ().TrimStart ('.').ToLowerInvariant ();
  }
}

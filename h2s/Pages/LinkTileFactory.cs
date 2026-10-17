using h2s.Models;
using h2s.Widgets;

namespace h2s.Pages;

/// <summary>
///   Builds dashboard tiles (<see cref="LinkTile" />) for one set of dashboard settings. The dashboard uses it to render its
///   tiles, and the editor uses it to render previews and refreshed tiles that look the same.
/// </summary>
public sealed class LinkTileFactory
{
  private readonly HashSet<string> localDomains;

  /// <summary>
  ///   Initializes a new instance of the <see cref="LinkTileFactory" /> class.
  /// </summary>
  /// <param name="settings">The dashboard settings, whose local domains decide which links are external.</param>
  public LinkTileFactory (DashboardSettings settings)
    => this.localDomains = LinkTileFactory.ParseLocalDomains (settings.LocalDomains);

  /// <summary>
  ///   Builds the view model for a link's tile.
  /// </summary>
  /// <param name="link">The link to show.</param>
  /// <param name="widgets">The link's widgets, in order; none when <c>null</c>.</param>
  /// <returns>The tile, with its external flag and its widgets.</returns>
  public LinkTile Create (Link link, IReadOnlyList<WidgetView>? widgets = null)
    => new (Link: link, IsExternal: this.IsExternalLink (link.Url)) { Widgets = widgets ?? [] };

  /// <summary>
  ///   Determines whether a link target should be marked as external based on the configured local domains.
  /// </summary>
  /// <param name="url">The absolute URL to evaluate.</param>
  /// <returns><c>true</c> when the URL points to an external host; otherwise, <c>false</c>.</returns>
  public bool IsExternalLink (string url)
  {
    if (this.localDomains.Count == 0)
    {
      return false;
    }

    if (!Uri.TryCreate (uriString: url, uriKind: UriKind.Absolute, result: out Uri? uri) || string.IsNullOrWhiteSpace (uri.Host))
    {
      return false;
    }

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
    {
      return parsedDomains;
    }

    var separators = new[] { ',', ';', '\n', '\r', '\t', ' ' };

    foreach (string value in domains.Split (separator: separators,
                                            options: StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      string normalized = LinkTileFactory.NormalizeDomain (value);

      if (!string.IsNullOrWhiteSpace (normalized))
      {
        _ = parsedDomains.Add (normalized);
      }
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
    {
      return "";
    }

    if (Uri.TryCreate (uriString: domain.Trim (), uriKind: UriKind.Absolute, result: out Uri? parsedUri) &&
        !string.IsNullOrWhiteSpace (parsedUri.Host))
    {
      return parsedUri.Host.Trim ().TrimStart ('.').ToLowerInvariant ();
    }

    return domain.Trim ().TrimStart ('.').ToLowerInvariant ();
  }
}

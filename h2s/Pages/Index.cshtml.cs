using h2s.Data;
using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages;

/// <summary>
///   Page model for the public dashboard home page.
/// </summary>
public class IndexModel : PageModel
{
  private readonly DashboardContext         context;
  private readonly DashboardSettingsService settingsService;

  /// <summary>
  ///   Initializes a new instance of the <see cref="IndexModel" /> class.
  /// </summary>
  /// <param name="context">The database context used to load categories and links.</param>
  /// <param name="settingsService">The service used to load dashboard settings.</param>
  public IndexModel (DashboardContext context, DashboardSettingsService settingsService)
  {
    this.context         = context;
    this.settingsService = settingsService;
  }

  /// <summary>
  ///   Gets the ordered set of categories and links displayed on the dashboard.
  /// </summary>
  public List<Category> Categories { get; set; } = new ();

  /// <summary>
  ///   Gets the infrastructure groups for displaying Uptime Kuma monitoring badges.
  /// </summary>
  public List<InfrastructureGroup> InfrastructureGroups { get; set; } = new ();

  /// <summary>
  ///   Gets the Uptime Kuma server URL if configured.
  /// </summary>
  public string? UptimeKumaServerUrl { get; set; }

  /// <summary>
  ///   Gets the Uptime Kuma status page slug if configured.
  /// </summary>
  public string? UptimeKumaStatusPageSlug { get; set; }

  /// <summary>
  ///   Gets the default duration in hours for uptime badges.
  /// </summary>
  public int UptimeKumaDefaultDuration { get; set; }

  /// <summary>
  ///   Gets a value indicating whether Uptime Kuma integration is configured.
  /// </summary>
  public bool IsUptimeKumaConfigured => !string.IsNullOrWhiteSpace (this.UptimeKumaServerUrl);

  private HashSet<string> LocalDomains { get; set; } = new ();

  /// <summary>
  ///   Loads the dashboard categories and local-domain settings required by the page.
  /// </summary>
  public async Task OnGetAsync ()
  {
    this.Categories = await this.context.Categories
                                .Include (c => c.Links)
                                .OrderBy (c => c.IsAdminCategory)
                                .ThenBy (c => c.Name)
                                .ToListAsync ();

    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();
    this.LocalDomains = IndexModel.ParseLocalDomains (settings.LocalDomains);

    // Load Uptime Kuma configuration
    this.UptimeKumaServerUrl       = settings.UptimeKumaServerUrl;
    this.UptimeKumaStatusPageSlug  = settings.UptimeKumaStatusPageSlug;
    this.UptimeKumaDefaultDuration = settings.UptimeKumaDefaultDuration;

    // Load infrastructure groups if Uptime Kuma is configured
    if (this.IsUptimeKumaConfigured)
    {
      this.InfrastructureGroups = await this.context.InfrastructureGroups
                                            .OrderBy (g => g.SortOrder)
                                            .ThenBy (g => g.Name)
                                            .ToListAsync ();
    }
  }

  /// <summary>
  ///   Builds the URL to the Uptime Kuma status page.
  /// </summary>
  /// <returns>The status page URL, or an empty string if not configured.</returns>
  public string BuildStatusPageUrl ()
  {
    if (string.IsNullOrWhiteSpace (this.UptimeKumaServerUrl) || string.IsNullOrWhiteSpace (this.UptimeKumaStatusPageSlug))
      return "";

    return $"{this.UptimeKumaServerUrl.TrimEnd ('/')}/status/{this.UptimeKumaStatusPageSlug}";
  }

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
    if (string.IsNullOrWhiteSpace (this.UptimeKumaServerUrl))
      return "";

    var baseUrl = $"{this.UptimeKumaServerUrl.TrimEnd ('/')}/api/badge/{monitorId}/{type}";

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
    if (this.LocalDomains.Count == 0)
      return false;

    if (!Uri.TryCreate (uriString: url, uriKind: UriKind.Absolute, result: out Uri? uri) || string.IsNullOrWhiteSpace (uri.Host))
      return false;

    string host = uri.Host.ToLowerInvariant ();
    bool isInternal = this.LocalDomains.Any (domain => (host == domain) ||
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
      string normalized = IndexModel.NormalizeDomain (value);
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

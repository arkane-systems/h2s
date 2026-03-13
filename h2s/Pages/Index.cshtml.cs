using h2s.Data;
using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages;

/// <summary>
/// Page model for the public dashboard home page.
/// </summary>
public class IndexModel : PageModel
{
  private readonly DashboardContext _context;
  private readonly DashboardSettingsService _settingsService;

  /// <summary>
  /// Gets the ordered set of categories and links displayed on the dashboard.
  /// </summary>
  public List<Category> Categories { get; set; } = new ();

  private HashSet<string> LocalDomains { get; set; } = new ();

  /// <summary>
  /// Initializes a new instance of the <see cref="IndexModel"/> class.
  /// </summary>
  /// <param name="context">The database context used to load categories and links.</param>
  /// <param name="settingsService">The service used to load dashboard settings.</param>
  public IndexModel (DashboardContext context, DashboardSettingsService settingsService)
  {
    this._context = context;
    this._settingsService = settingsService;
  }

  /// <summary>
  /// Loads the dashboard categories and local-domain settings required by the page.
  /// </summary>
  public async Task OnGetAsync ()
  {
    this.Categories = await this._context.Categories
      .Include (c => c.Links)
      .OrderBy (c => c.IsAdminCategory)
      .ThenBy (c => c.Name)
      .ToListAsync ();

    var settings = await this._settingsService.GetSettingsAsync ();
    this.LocalDomains = ParseLocalDomains (settings.LocalDomains);
  }

  /// <summary>
  /// Determines whether a link target should be marked as external based on the configured local domains.
  /// </summary>
  /// <param name="url">The absolute URL to evaluate.</param>
  /// <returns><c>true</c> when the URL points to an external host; otherwise, <c>false</c>.</returns>
  public bool IsExternalLink (string url)
  {
    if (this.LocalDomains.Count == 0)
    {
      return false;
    }

    if (!Uri.TryCreate (url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace (uri.Host))
    {
      return false;
    }

    var host = uri.Host.ToLowerInvariant ();
    var isInternal = this.LocalDomains.Any (domain => host == domain || host.EndsWith ($".{domain}"));
    return !isInternal;
  }

  /// <summary>
  /// Parses the configured local-domain list into a normalized set of unique host names.
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
    foreach (var value in domains.Split (separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      var normalized = NormalizeDomain (value);
      if (!string.IsNullOrWhiteSpace (normalized))
      {
        _ = parsedDomains.Add (normalized);
      }
    }

    return parsedDomains;
  }

  /// <summary>
  /// Normalizes a configured domain or URL down to its lowercase host name.
  /// </summary>
  /// <param name="domain">The raw domain or URL value.</param>
  /// <returns>The normalized host name, or an empty string when the input is blank.</returns>
  private static string NormalizeDomain (string domain)
  {
    if (string.IsNullOrWhiteSpace (domain))
    {
      return "";
    }

    if (Uri.TryCreate (domain.Trim (), UriKind.Absolute, out var parsedUri) && !string.IsNullOrWhiteSpace (parsedUri.Host))
    {
      return parsedUri.Host.Trim ().TrimStart ('.').ToLowerInvariant ();
    }

    return domain.Trim ().TrimStart ('.').ToLowerInvariant ();
  }
}

using h2s.Data;
using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages;

public class IndexModel : PageModel
{
  private readonly DashboardContext _context;
  private readonly DashboardSettingsService _settingsService;

  public List<Category> Categories { get; set; } = new ();
  private HashSet<string> LocalDomains { get; set; } = new ();

  public IndexModel (DashboardContext context, DashboardSettingsService settingsService)
  {
    this._context = context;
    this._settingsService = settingsService;
  }

  public async Task OnGetAsync ()
  {
    this.Categories = await this._context.Categories
      .Include (c => c.Links)
      .OrderBy (c => c.IsAdminCategory)
      .ThenBy (c => c.Name)
      .ToListAsync ();

    var settings = await this._settingsService.GetSettingsAsync ();
    this.LocalDomains = ParseLocalDomains (settings.LocalDomain);
  }

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

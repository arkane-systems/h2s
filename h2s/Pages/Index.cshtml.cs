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
  public string LocalDomain { get; private set; } = "";

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
    this.LocalDomain = NormalizeDomain (settings.LocalDomain);
  }

  public bool IsExternalLink (string url)
  {
    if (string.IsNullOrWhiteSpace (this.LocalDomain))
    {
      return false;
    }

    if (!Uri.TryCreate (url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace (uri.Host))
    {
      return false;
    }

    var host = uri.Host.ToLowerInvariant ();
    return host != this.LocalDomain && !host.EndsWith ($".{this.LocalDomain}");
  }

  private static string NormalizeDomain (string domain)
  {
    if (string.IsNullOrWhiteSpace (domain))
    {
      return "";
    }

    return domain.Trim ().TrimStart ('.').ToLowerInvariant ();
  }
}

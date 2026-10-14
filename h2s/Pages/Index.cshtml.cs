using h2s.Authorization;
using h2s.Data;
using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages;

/// <summary>
///   Page model for the public dashboard home page.
/// </summary>
public class IndexModel : PageModel
{
  private readonly IAuthorizationService    authorizationService;
  private readonly DashboardContext         context;
  private readonly DashboardSettingsService settingsService;

  private LinkTileFactory tiles = new (new DashboardSettings ());

  /// <summary>
  ///   Initializes a new instance of the <see cref="IndexModel" /> class.
  /// </summary>
  /// <param name="context">The database context used to load categories and links.</param>
  /// <param name="settingsService">The service used to load dashboard settings.</param>
  /// <param name="authorizationService">The service used to check whether the user may enter edit mode.</param>
  public IndexModel (DashboardContext         context,
                     DashboardSettingsService settingsService,
                     IAuthorizationService    authorizationService)
  {
    this.context              = context;
    this.settingsService      = settingsService;
    this.authorizationService = authorizationService;
  }

  /// <summary>
  ///   Gets the ordered set of categories and links displayed on the dashboard.
  /// </summary>
  public List<Category> Categories { get; set; } = new ();

  /// <summary>
  ///   Gets a value indicating whether the dashboard is in edit mode, where categories and links can be rearranged.
  /// </summary>
  public bool IsEditMode { get; private set; }

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

  /// <summary>
  ///   Loads the dashboard categories and local-domain settings required by the page.
  /// </summary>
  /// <param name="edit">Whether edit mode is requested. It is granted only to users who satisfy the Admin policy.</param>
  public async Task OnGetAsync (bool edit = false)
  {
    this.IsEditMode = edit &&
                      (await this.authorizationService.AuthorizeAsync (user: this.User, policyName: AdminRequirement.PolicyName))
                     .Succeeded;

    this.Categories = await this.context.Categories
                                .Include (c => c.Links.OrderBy (l => l.SortOrder).ThenBy (l => l.Label))
                                .OrderBy (c => c.IsAdminCategory)
                                .ThenBy (c => c.SortOrder)
                                .ThenBy (c => c.Name)
                                .ToListAsync ();

    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();
    this.tiles = new LinkTileFactory (settings);

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
    => this.tiles.BuildBadgeUrl (monitorId: monitorId, type: type, duration: duration, label: label);

  /// <summary>
  ///   Builds the view model for a link's tile.
  /// </summary>
  /// <param name="link">The link to show.</param>
  /// <returns>The tile, with its external flag and, when Uptime Kuma is configured and the link has a monitor, its status badge.</returns>
  public LinkTile CreateLinkTile (Link link) => this.tiles.Create (link);

  /// <summary>
  ///   Determines whether a link target should be marked as external based on the configured local domains.
  /// </summary>
  /// <param name="url">The absolute URL to evaluate.</param>
  /// <returns><c>true</c> when the URL points to an external host; otherwise, <c>false</c>.</returns>
  public bool IsExternalLink (string url) => this.tiles.IsExternalLink (url);
}

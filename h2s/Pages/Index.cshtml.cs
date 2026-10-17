using h2s.Authorization;
using h2s.Data;
using h2s.Models;
using h2s.Panels;
using h2s.Services;
using h2s.Widgets;
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
  private readonly PanelService             panelService;
  private readonly DashboardSettingsService settingsService;
  private readonly WidgetService            widgetService;

  private LinkTileFactory tiles = new (new DashboardSettings ());

  private Dictionary<int, List<WidgetView>> widgets = [];

  /// <summary>
  ///   Initializes a new instance of the <see cref="IndexModel" /> class.
  /// </summary>
  /// <param name="context">The database context used to load categories and links.</param>
  /// <param name="settingsService">The service used to load dashboard settings.</param>
  /// <param name="panelService">The service used to load the panels above and below the categories.</param>
  /// <param name="authorizationService">The service used to check whether the user may enter edit mode.</param>
  /// <param name="widgetService">The service used to load the links' widgets.</param>
  public IndexModel (DashboardContext         context,
                     DashboardSettingsService settingsService,
                     PanelService             panelService,
                     IAuthorizationService    authorizationService,
                     WidgetService            widgetService)
  {
    this.widgetService        = widgetService;
    this.panelService         = panelService;
    this.context              = context;
    this.settingsService      = settingsService;
    this.authorizationService = authorizationService;
  }

  /// <summary>
  ///   Gets the ordered set of categories and links displayed on the dashboard.
  /// </summary>
  public List<Category> Categories { get; set; } = new ();

  /// <summary>
  ///   Gets the dashboard's title, for the page's heading.
  /// </summary>
  public string DashboardTitle { get; private set; } = "";

  /// <summary>
  ///   Gets a value indicating whether the dashboard is in edit mode, where categories and links can be rearranged.
  /// </summary>
  public bool IsEditMode { get; private set; }

  /// <summary>
  ///   Gets the panels above the categories, in order. Outside edit mode, panels with nothing to show are left out.
  /// </summary>
  public List<PanelView> TopPanels { get; private set; } = new ();

  /// <summary>
  ///   Gets the panels below the categories, in order. Outside edit mode, panels with nothing to show are left out.
  /// </summary>
  public List<PanelView> BottomPanels { get; private set; } = new ();

  /// <summary>
  ///   Gets the search engines quick launch offers when nothing on the dashboard matches, in Settings order. Empty in edit
  ///   mode, which has no quick launch.
  /// </summary>
  public List<SearchPanelEngine> QuickLaunchEngines { get; private set; } = new ();

  /// <summary>
  ///   Gets the categories this browser has collapsed. Empty in edit mode, which always shows every category's links.
  /// </summary>
  public IReadOnlySet<int> CollapsedCategoryIds { get; private set; } = new HashSet<int> ();

  /// <summary>
  ///   Loads the dashboard's categories, settings and panels.
  /// </summary>
  /// <param name="edit">Whether edit mode is requested. It is granted only to users who satisfy the Admin policy.</param>
  /// <param name="cancellationToken">A token that is cancelled when the request is aborted.</param>
  public async Task OnGetAsync (bool edit = false, CancellationToken cancellationToken = default)
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

    this.widgets =
      await this.widgetService.GetForLinksAsync (linkIds: [.. this.Categories.SelectMany (c => c.Links).Select (l => l.Id)],
                                                 cancellationToken: cancellationToken);

    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();
    this.tiles          = new LinkTileFactory (settings);
    this.DashboardTitle = settings.Title;

    List<PanelView> panels = (await this.panelService.GetPanelsAsync (cancellationToken))
                            .Where (p => this.IsEditMode || !p.Content.IsEmpty)
                            .ToList ();

    this.TopPanels    = panels.Where (p => p.Zone == PanelZone.Top).ToList ();
    this.BottomPanels = panels.Where (p => p.Zone == PanelZone.Bottom).ToList ();

    // Quick launch and collapsed categories are for using the dashboard, so edit mode leaves them out.
    if (!this.IsEditMode)
    {
      this.CollapsedCategoryIds = ViewerPreferences.ReadCollapsedCategories (this.Request);

      List<SearchEngine> engines = await this.context.SearchEngines
                                             .AsNoTracking ()
                                             .OrderBy (e => e.SortOrder)
                                             .ThenBy (e => e.Name)
                                             .ToListAsync (cancellationToken);
      SearchEngine? defaultEngine = WebSearch.GetDefault (engines);

      this.QuickLaunchEngines =
      [
        .. engines.Select (e => new SearchPanelEngine (Id: e.Id,
                                                       Name: e.Name,
                                                       Shortcut: e.Shortcut,
                                                       IsDefault: e == defaultEngine)),
      ];
    }
  }

  /// <summary>
  ///   Builds the view model for a link's tile.
  /// </summary>
  /// <param name="link">The link to show.</param>
  /// <returns>The tile, with its external flag and, when Uptime Kuma is configured and the link has a monitor, its status badge.</returns>
  public LinkTile CreateLinkTile (Link link) => this.tiles.Create (link: link, widgets: this.widgets.GetValueOrDefault (link.Id));

  /// <summary>
  ///   Determines whether a link target should be marked as external based on the configured local domains.
  /// </summary>
  /// <param name="url">The absolute URL to evaluate.</param>
  /// <returns><c>true</c> when the URL points to an external host; otherwise, <c>false</c>.</returns>
  public bool IsExternalLink (string url) => this.tiles.IsExternalLink (url);
}

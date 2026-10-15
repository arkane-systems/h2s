using h2s.Data;
using h2s.Models;
using Microsoft.EntityFrameworkCore;

namespace h2s.Panels;

/// <summary>
///   Loads the dashboard's panels and builds their content through their kinds.
/// </summary>
public sealed partial class PanelService
{
  private readonly PanelCatalog          catalog;
  private readonly DashboardContext      context;
  private readonly ILogger<PanelService> logger;
  private readonly IServiceProvider      services;

  /// <summary>
  ///   Initializes a new instance of the <see cref="PanelService" /> class.
  /// </summary>
  /// <param name="context">The database context holding the panels.</param>
  /// <param name="catalog">The known panel kinds.</param>
  /// <param name="services">The request's services, passed on to the kinds.</param>
  /// <param name="logger">The logger for panels that fail to build.</param>
  public PanelService (DashboardContext context, PanelCatalog catalog, IServiceProvider services, ILogger<PanelService> logger)
  {
    this.context  = context;
    this.catalog  = catalog;
    this.services = services;
    this.logger   = logger;
  }

  /// <summary>
  ///   Loads every panel and builds its content. A panel whose kind is unknown, or whose kind fails, comes back empty
  ///   rather than breaking the page.
  /// </summary>
  /// <param name="cancellationToken">A token used to cancel the work.</param>
  /// <returns>The panels, top zone first, each zone in order.</returns>
  public async Task<List<PanelView>> GetPanelsAsync (CancellationToken cancellationToken = default)
  {
    List<Panel> panels = await this.context.Panels
                                   .AsNoTracking ()
                                   .OrderBy (p => p.Zone)
                                   .ThenBy (p => p.SortOrder)
                                   .ThenBy (p => p.Id)
                                   .ToListAsync (cancellationToken);

    var views = new List<PanelView> (panels.Count);

    foreach (Panel panel in panels)
      views.Add (await this.CreateViewAsync (panel: panel, cancellationToken: cancellationToken));

    return views;
  }

  /// <summary>
  ///   Loads one panel and builds its content.
  /// </summary>
  /// <param name="id">The panel's ID.</param>
  /// <param name="cancellationToken">A token used to cancel the work.</param>
  /// <returns>The panel, or <c>null</c> when it doesn't exist.</returns>
  public async Task<PanelView?> GetPanelAsync (int id, CancellationToken cancellationToken = default)
  {
    Panel? panel = await this.context.Panels
                             .AsNoTracking ()
                             .FirstOrDefaultAsync (predicate: p => p.Id == id, cancellationToken: cancellationToken);

    return panel == null ? null : await this.CreateViewAsync (panel: panel, cancellationToken: cancellationToken);
  }

  /// <summary>
  ///   Builds one panel's content.
  /// </summary>
  /// <param name="panel">The panel.</param>
  /// <param name="cancellationToken">A token used to cancel the work.</param>
  /// <returns>The panel, ready to render.</returns>
  private async Task<PanelView> CreateViewAsync (Panel panel, CancellationToken cancellationToken)
  {
    if (this.catalog.Find (panel.Kind) is not { } kind)
    {
      return new PanelView (Id: panel.Id,
                            Kind: panel.Kind,
                            DisplayName: panel.Kind,
                            Zone: panel.Zone,
                            Content: PanelContent
                             .Empty ($"This version of h2s doesn't know this panel's kind (\"{panel.Kind}\")."));
    }

    PanelContent content;

    try
    {
      content =
        await kind.GetContentAsync (new PanelContext (Options:
                                                      new PanelOptionValues (schema: kind.Options, json: panel.OptionsJson),
                                                      Services: this.services,
                                                      CancellationToken: cancellationToken));
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      this.LogPanelFailed (exception: ex, panelId: panel.Id, kind: panel.Kind);
      content = PanelContent.Empty ("This panel couldn't be shown; the server log has the details.");
    }

    return new PanelView (Id: panel.Id, Kind: kind.Key, DisplayName: kind.DisplayName, Zone: panel.Zone, Content: content);
  }

  /// <summary>
  ///   Logs a panel whose kind failed to build its content.
  /// </summary>
  /// <param name="exception">What went wrong.</param>
  /// <param name="panelId">The panel's ID.</param>
  /// <param name="kind">The panel's kind key.</param>
  [LoggerMessage (Level = LogLevel.Warning, Message = "Panel {PanelId} ({Kind}) couldn't be shown.")]
  private partial void LogPanelFailed (Exception exception, int panelId, string kind);
}

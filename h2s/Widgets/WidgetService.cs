using System.Security.Cryptography;
using System.Text;
using h2s.Data;
using h2s.Integrations;
using h2s.Models;
using h2s.Widgets.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace h2s.Widgets;

/// <summary>
///   Loads links' widgets and reads them through their kinds. Rendering a page only uses results already read, so a slow
///   service never holds the page up; the page reads the rest through <c>/widgets/{id}</c>. The link dialog's preview
///   does the same with widgets that aren't saved yet (drafts), which share their results with saved widgets of the same
///   settings.
/// </summary>
public sealed class WidgetService
{
  private readonly WidgetCatalog     catalog;
  private readonly ConnectionService connections;
  private readonly DashboardContext  context;
  private readonly WidgetFetcher     fetcher;
  private readonly IServiceProvider  services;

  /// <summary>
  ///   Initializes a new instance of the <see cref="WidgetService" /> class.
  /// </summary>
  /// <param name="context">The database context holding the widgets.</param>
  /// <param name="connections">The service holding the connections the widgets use.</param>
  /// <param name="catalog">The known widget kinds.</param>
  /// <param name="fetcher">Reads widgets and keeps their results.</param>
  /// <param name="services">The request's services, passed on to the kinds.</param>
  public WidgetService (DashboardContext  context,
                        ConnectionService connections,
                        WidgetCatalog     catalog,
                        WidgetFetcher     fetcher,
                        IServiceProvider  services)
  {
    this.context     = context;
    this.connections = connections;
    this.catalog     = catalog;
    this.fetcher     = fetcher;
    this.services    = services;
  }

  /// <summary>
  ///   Loads the widgets of some links, each with its result if one was read recently.
  /// </summary>
  /// <param name="linkIds">The links.</param>
  /// <param name="cancellationToken">A token used to cancel the queries.</param>
  /// <returns>Each link's widgets, in order, keyed by link ID; links without widgets are left out.</returns>
  public async Task<Dictionary<int, List<WidgetView>>> GetForLinksAsync (IReadOnlyCollection<int> linkIds,
                                                                         CancellationToken        cancellationToken = default)
  {
    if (linkIds.Count == 0)
    {
      return [];
    }

    List<WidgetView> views = await this.DescribeAsync (query: this.context.Widgets.Where (w => (w.LinkId != null) &&
                                                                                               linkIds.Contains (w.LinkId.Value)),
                                                       cancellationToken: cancellationToken);

    return views.GroupBy (v => v.LinkId).ToDictionary (keySelector: g => g.Key, elementSelector: g => g.ToList ());
  }

  /// <summary>
  ///   Loads a panel's widgets, each with its result if one was read recently.
  /// </summary>
  /// <param name="panelId">The panel.</param>
  /// <param name="cancellationToken">A token used to cancel the queries.</param>
  /// <returns>The panel's widgets, in order.</returns>
  public async Task<List<WidgetView>> GetForPanelAsync (int panelId, CancellationToken cancellationToken = default)
    => await this.DescribeAsync (query: this.context.Widgets.Where (w => w.PanelId == panelId),
                                 cancellationToken: cancellationToken);

  /// <summary>
  ///   Loads one widget and reads it, unless it was read recently.
  /// </summary>
  /// <param name="id">The widget's ID.</param>
  /// <param name="cancellationToken">A token used to cancel the query.</param>
  /// <returns>The widget with its result, or <c>null</c> when it doesn't exist.</returns>
  public async Task<WidgetView?> ReadAsync (int id, CancellationToken cancellationToken = default)
  {
    Widget? widget = await this.context.Widgets
                               .AsNoTracking ()
                               .FirstOrDefaultAsync (predicate: w => w.Id == id, cancellationToken: cancellationToken);

    if (widget == null)
    {
      return null;
    }

    Connection? connection =
      (await this.connections.GetConnectionsAsync (cancellationToken)).FirstOrDefault (c => c.Id == widget.ConnectionId);

    return await this.ReadAsync (widget: widget, connection: connection);
  }

  /// <summary>
  ///   Describes widgets that aren't saved, for the link dialog's preview, each with its result if a widget of the same
  ///   settings was read recently.
  /// </summary>
  /// <param name="plan">The widgets, as <see cref="WidgetForm" /> read them.</param>
  /// <param name="cancellationToken">A token used to cancel the query.</param>
  /// <returns>The widgets, in order, each with its position as its <see cref="WidgetView.PreviewIndex" />.</returns>
  public async Task<List<WidgetView>> DescribeDraftsAsync (IReadOnlyList<WidgetPlan> plan,
                                                           CancellationToken         cancellationToken = default)
  {
    List<Connection> connections = await this.connections.GetConnectionsAsync (cancellationToken);

    return
    [
      .. plan.Select ((p, i) =>
                      {
                        Widget widget = WidgetService.Draft (plan: p, index: i);

                        return this.Describe (widget: widget,
                                              connection: connections.FirstOrDefault (c => c.Id == widget.ConnectionId)) with
                               {
                                 PreviewIndex = i,
                               };
                      }),
    ];
  }

  /// <summary>
  ///   Reads one widget that isn't saved, for the link dialog's preview, unless a widget of the same settings was read
  ///   recently. What it reads is kept for saved widgets of the same settings too.
  /// </summary>
  /// <param name="plan">The widgets, as <see cref="WidgetForm" /> read them.</param>
  /// <param name="index">The position of the widget to read.</param>
  /// <param name="cancellationToken">A token used to cancel the query.</param>
  /// <returns>The widget with its result, or <c>null</c> when there's no widget at that position.</returns>
  public async Task<WidgetView?> ReadDraftAsync (IReadOnlyList<WidgetPlan> plan,
                                                 int                       index,
                                                 CancellationToken         cancellationToken = default)
  {
    if ((index < 0) || (index >= plan.Count))
    {
      return null;
    }

    Widget widget = WidgetService.Draft (plan: plan[index], index: index);
    Connection? connection =
      (await this.connections.GetConnectionsAsync (cancellationToken)).FirstOrDefault (c => c.Id == widget.ConnectionId);

    return await this.ReadAsync (widget: widget, connection: connection) with { PreviewIndex = index };
  }

  /// <summary>
  ///   Gets how often a widget is read, in seconds: its own interval, or its kind's default, never under the minimum.
  /// </summary>
  /// <param name="widget">The widget.</param>
  /// <param name="kind">Its kind, if known.</param>
  /// <returns>The interval.</returns>
  public static int RefreshSecondsOf (Widget widget, IWidgetKind? kind)
    => Math.Max (val1: widget.RefreshSeconds ?? (int)(kind?.DefaultRefresh.TotalSeconds ?? 60), val2: Widget.MinRefreshSeconds);

  /// <summary>
  ///   Loads widgets, in order, and describes each with its cached result.
  /// </summary>
  /// <param name="query">The widgets to load.</param>
  /// <param name="cancellationToken">A token used to cancel the queries.</param>
  /// <returns>The widgets.</returns>
  private async Task<List<WidgetView>> DescribeAsync (IQueryable<Widget> query, CancellationToken cancellationToken)
  {
    List<Widget> widgets = await query.AsNoTracking ()
                                      .OrderBy (w => w.SortOrder)
                                      .ThenBy (w => w.Id)
                                      .ToListAsync (cancellationToken);

    Dictionary<int, Connection> connectionsById =
      (await this.connections.GetConnectionsAsync (cancellationToken)).ToDictionary (c => c.Id);

    return [.. widgets.Select (w => this.Describe (widget: w, connection: connectionsById.GetValueOrDefault (w.ConnectionId)))];
  }

  /// <summary>
  ///   Makes an unsaved widget of a planned one, so drafts are described and read as saved widgets are.
  /// </summary>
  /// <param name="plan">The planned widget.</param>
  /// <param name="index">Its position.</param>
  /// <returns>The widget, with no ID or link.</returns>
  private static Widget Draft (WidgetPlan plan, int index)
    => new ()
       {
         ConnectionId   = plan.ConnectionId,
         Kind           = plan.Kind,
         OptionsJson    = plan.OptionsJson,
         RefreshSeconds = plan.RefreshSeconds,
         Label          = plan.Label,
         SortOrder      = index,
       };

  /// <summary>
  ///   Reads a widget through its kind, unless it was read recently.
  /// </summary>
  /// <param name="widget">The widget.</param>
  /// <param name="connection">Its connection, if it still exists.</param>
  /// <returns>The widget with its result.</returns>
  private async Task<WidgetView> ReadAsync (Widget widget, Connection? connection)
  {
    WidgetView view = this.Describe (widget: widget, connection: connection);

    if ((view.Result != null) ||
        (connection  == null) ||
        this.catalog.Find (integrationKey: connection.IntegrationKey, kindKey: widget.Kind) is not { } entry)
    {
      return view;
    }

    OptionValues connectionOptions = this.connections.ReadOptions (connection)!;
    var          options           = new OptionValues (schema: entry.Kind.Options, json: widget.OptionsJson);

    WidgetResult result = await this.fetcher.GetAsync (key: WidgetService.CacheKey (widget: widget,
                                                                                    connection: connection,
                                                                                    refreshSeconds: view.RefreshSeconds),
                                                       widgetId: widget.Id,
                                                       lifetime: TimeSpan.FromSeconds (view.RefreshSeconds),
                                                       fetch: token
                                                                => entry.Kind.FetchAsync (new WidgetContext (Connection:
                                                                                                             connectionOptions,
                                                                                                             Options: options,
                                                                                                             Services: this
                                                                                                              .services,
                                                                                                             CancellationToken:
                                                                                                             token)));

    return view with { Result = result };
  }

  /// <summary>
  ///   Describes a widget, with its cached result, or with why it can't be read.
  /// </summary>
  /// <param name="widget">The widget.</param>
  /// <param name="connection">Its connection, if it still exists.</param>
  /// <returns>The widget's view.</returns>
  private WidgetView Describe (Widget widget, Connection? connection)
  {
    WidgetKindEntry? entry = connection == null
                               ? null
                               : this.catalog.Find (integrationKey: connection.IntegrationKey, kindKey: widget.Kind);
    int refreshSeconds = WidgetService.RefreshSecondsOf (widget: widget, kind: entry?.Kind);
    WidgetResult? result = entry == null
                             ? WidgetResult.Unknown ("This version of h2s doesn't know this kind of widget.")
                             : this.fetcher.GetCached (WidgetService.CacheKey (widget: widget,
                                                                               connection: connection!,
                                                                               refreshSeconds: refreshSeconds));

    return new WidgetView (Id: widget.Id,
                           LinkId: widget.LinkId  ?? 0,
                           Role: entry?.Kind.Role ?? WidgetRole.Status,
                           SortOrder: widget.SortOrder,
                           RefreshSeconds: refreshSeconds,
                           Result: result) { Label = widget.Label };
  }

  /// <summary>
  ///   Builds a widget's cache key from its settings and its connection's, so it changes whenever they do. It leaves out
  ///   the widget's ID, so widgets of the same settings, saved or not (such as one in the link dialog's preview), share
  ///   one result.
  /// </summary>
  /// <param name="widget">The widget.</param>
  /// <param name="connection">Its connection.</param>
  /// <param name="refreshSeconds">How long its result is kept, in seconds.</param>
  /// <returns>The key.</returns>
  private static string CacheKey (Widget widget, Connection connection, int refreshSeconds)
  {
    byte[] hash = SHA256.HashData (Encoding.UTF8.GetBytes (string.Join (separator: '\n',
                                                                        connection.Id,
                                                                        connection.OptionsJson,
                                                                        connection.SecretsJson,
                                                                        widget.Kind,
                                                                        widget.OptionsJson,
                                                                        refreshSeconds)));

    return $"widget:{Convert.ToHexString (inArray: hash, offset: 0, length: 16)}";
  }
}

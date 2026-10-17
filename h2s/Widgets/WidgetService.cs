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
///   service never holds the page up; the page reads the rest through <c>/widgets/{id}</c>.
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

    List<Widget> widgets = await this.context.Widgets
                                     .AsNoTracking ()
                                     .Where (w => linkIds.Contains (w.LinkId))
                                     .OrderBy (w => w.SortOrder)
                                     .ThenBy (w => w.Id)
                                     .ToListAsync (cancellationToken);

    Dictionary<int, Connection> connectionsById =
      (await this.connections.GetConnectionsAsync (cancellationToken)).ToDictionary (c => c.Id);

    return widgets.Select (w => this.Describe (widget: w, connection: connectionsById.GetValueOrDefault (w.ConnectionId)))
                  .GroupBy (v => v.LinkId)
                  .ToDictionary (keySelector: g => g.Key, elementSelector: g => g.ToList ());
  }

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
    WidgetView view = this.Describe (widget: widget, connection: connection);

    if ((view.Result != null) ||
        (connection  == null) ||
        this.catalog.Find (integrationKey: connection.IntegrationKey, kindKey: widget.Kind) is not { } entry)
    {
      return view;
    }

    OptionValues connectionOptions = this.connections.ReadOptions (connection)!;
    var          options           = new OptionValues (schema: entry.Kind.Options, json: widget.OptionsJson);

    WidgetResult result = await this.fetcher.GetAsync (key: WidgetService.CacheKey (widget: widget, connection: connection),
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
  ///   Gets how often a widget is read, in seconds: its own interval, or its kind's default, never under the minimum.
  /// </summary>
  /// <param name="widget">The widget.</param>
  /// <param name="kind">Its kind, if known.</param>
  /// <returns>The interval.</returns>
  public static int RefreshSecondsOf (Widget widget, IWidgetKind? kind)
    => Math.Max (val1: widget.RefreshSeconds ?? (int)(kind?.DefaultRefresh.TotalSeconds ?? 60), val2: Widget.MinRefreshSeconds);

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
    WidgetResult? result = entry == null
                             ? WidgetResult.Unknown ("This version of h2s doesn't know this kind of widget.")
                             : this.fetcher.GetCached (WidgetService.CacheKey (widget: widget, connection: connection!));

    return new WidgetView (Id: widget.Id,
                           LinkId: widget.LinkId,
                           Role: entry?.Kind.Role ?? WidgetRole.Status,
                           SortOrder: widget.SortOrder,
                           RefreshSeconds: WidgetService.RefreshSecondsOf (widget: widget, kind: entry?.Kind),
                           Result: result);
  }

  /// <summary>
  ///   Builds a widget's cache key, which changes whenever the widget's or its connection's settings do.
  /// </summary>
  /// <param name="widget">The widget.</param>
  /// <param name="connection">Its connection.</param>
  /// <returns>The key.</returns>
  private static string CacheKey (Widget widget, Connection connection)
  {
    byte[] hash =
      SHA256.HashData (Encoding.UTF8.GetBytes ($"{widget.Kind}\n{widget.OptionsJson}\n{connection.OptionsJson}\n{connection.SecretsJson}"));

    return $"widget:{widget.Id}:{Convert.ToHexString (inArray: hash, offset: 0, length: 8)}";
  }
}

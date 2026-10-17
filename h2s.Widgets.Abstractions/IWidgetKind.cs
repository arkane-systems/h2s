namespace h2s.Widgets.Abstractions;

/// <summary>
///   A kind of widget an integration offers for links' tiles. It declares its options, and the core stores, checks and
///   edits them, caches what it reads, refreshes it and draws it; the kind only reads its service through the widget's
///   connection and returns a <see cref="WidgetResult" />.
/// </summary>
public interface IWidgetKind
{
  /// <summary>
  ///   Gets the key stored with each widget of the kind, unique within its integration. It never changes once widgets
  ///   exist.
  /// </summary>
  string Key { get; }

  /// <summary>
  ///   Gets the kind's name, as the link dialog shows it.
  /// </summary>
  string DisplayName { get; }

  /// <summary>
  ///   Gets what the kind's widgets are for, which decides where they sit on a tile.
  /// </summary>
  WidgetRole Role { get; }

  /// <summary>
  ///   Gets where the kind's widgets may go. By default a status widget suits tiles and status bars alike, and an app
  ///   widget only tiles; a kind whose widgets make sense in only one place says so.
  /// </summary>
  WidgetPlacements Placements
    => this.Role == WidgetRole.App ? WidgetPlacements.Tile : WidgetPlacements.Tile | WidgetPlacements.StatusBar;

  /// <summary>
  ///   Gets the kind's option schema.
  /// </summary>
  IReadOnlyList<SchemaOption> Options { get; }

  /// <summary>
  ///   Gets how often the kind's widgets are read again by default. A widget may choose its own interval, of at least 15
  ///   seconds.
  /// </summary>
  TimeSpan DefaultRefresh { get; }

  /// <summary>
  ///   Reads what a widget shows. A failure should come back as <see cref="WidgetResult.Unknown" /> with a reason; the
  ///   core treats an exception or a timeout the same way, so a widget never breaks the page.
  /// </summary>
  /// <param name="context">The widget's connection and options, and the request's services.</param>
  /// <returns>What the widget shows.</returns>
  Task<WidgetResult> FetchAsync (WidgetContext context);
}

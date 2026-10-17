namespace h2s.Widgets.Abstractions;

/// <summary>
///   What a widget kind is for, which decides where its widgets sit on a tile.
/// </summary>
public enum WidgetRole
{
  /// <summary>
  ///   A widget for links to one particular app, such as a Home Assistant entity or a Plex server. A link has at most one,
  ///   on the left of its tile's widget row.
  /// </summary>
  App = 0,

  /// <summary>
  ///   A widget that suits any link, such as an Uptime Kuma monitor's status. A link may have several, in an order of its
  ///   own, as compact pills on the right of its tile's widget row.
  /// </summary>
  Status = 1,
}

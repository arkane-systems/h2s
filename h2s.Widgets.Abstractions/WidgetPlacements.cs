namespace h2s.Widgets.Abstractions;

/// <summary>
///   Where a kind's widgets may go. A kind declares every place its widgets make sense, and the core offers and accepts it
///   only there.
/// </summary>
[Flags]
public enum WidgetPlacements
{
  /// <summary>Nowhere; the placement of a panel that holds no widgets.</summary>
  None = 0,

  /// <summary>On a link's tile, arranged by the kind's <see cref="WidgetRole" />.</summary>
  Tile = 1,

  /// <summary>On a status bar panel, as one of its items.</summary>
  StatusBar = 2,
}

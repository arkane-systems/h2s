using h2s.Models;

namespace h2s.Panels;

/// <summary>
///   The panels a dashboard starts with: the status bar, then search, above the categories. The AddPanels migration
///   creates the same two, and importing a file from before panels gives the dashboard these.
/// </summary>
public static class DefaultPanels
{
  /// <summary>
  ///   Creates the default panels, with default options.
  /// </summary>
  /// <returns>New, unsaved panels.</returns>
  public static List<Panel> Create ()
    =>
    [
      new () { Kind = StatusBarPanelKind.KindKey, Zone = PanelZone.Top, SortOrder = 0 },
      new () { Kind = SearchPanelKind.KindKey, Zone    = PanelZone.Top, SortOrder = 1 },
    ];
}

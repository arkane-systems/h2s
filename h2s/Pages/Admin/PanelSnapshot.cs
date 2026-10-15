using h2s.Models;

namespace h2s.Pages.Admin;

/// <summary>
///   Everything needed to recreate a deleted panel. The delete handler returns it, and Undo posts it back unchanged.
/// </summary>
/// <param name="Kind">The panel's kind key.</param>
/// <param name="Zone">Whether the panel sat above or below the categories.</param>
/// <param name="SortOrder">The panel's position within its zone.</param>
/// <param name="OptionsJson">The panel's stored options.</param>
public sealed record PanelSnapshot (string? Kind, PanelZone Zone, int SortOrder, string? OptionsJson)
{
  /// <summary>
  ///   Takes a snapshot of a panel.
  /// </summary>
  /// <param name="panel">The panel.</param>
  /// <param name="position">The panel's position within its zone, counting from 0.</param>
  /// <returns>The snapshot.</returns>
  public static PanelSnapshot From (Panel panel, int position)
    => new (Kind: panel.Kind, Zone: panel.Zone, SortOrder: position, OptionsJson: panel.OptionsJson);
}

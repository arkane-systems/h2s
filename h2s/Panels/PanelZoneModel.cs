using h2s.Models;

namespace h2s.Panels;

/// <summary>
///   The view model for one zone of panels on the dashboard (<c>Pages/Shared/_PanelZone.cshtml</c>).
/// </summary>
/// <param name="Zone">Which zone it is.</param>
/// <param name="Panels">The zone's panels, in order.</param>
/// <param name="IsEditMode">Whether the dashboard is in edit mode, where panels get handles and empty ones show why.</param>
public sealed record PanelZoneModel (PanelZone Zone, IReadOnlyList<PanelView> Panels, bool IsEditMode);

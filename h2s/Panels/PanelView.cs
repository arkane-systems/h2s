using h2s.Models;

namespace h2s.Panels;

/// <summary>
///   A panel ready to render on the dashboard.
/// </summary>
/// <param name="Id">The panel's ID.</param>
/// <param name="Kind">The panel's kind key.</param>
/// <param name="DisplayName">The kind's name, or the bare key when the kind is unknown.</param>
/// <param name="Zone">Whether the panel sits above or below the categories.</param>
/// <param name="Content">What the panel shows.</param>
public sealed record PanelView (int Id, string Kind, string DisplayName, PanelZone Zone, PanelContent Content);

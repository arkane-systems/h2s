namespace h2s.Panels;

/// <summary>
///   The view model for the search panel (<c>Pages/Shared/_SearchPanel.cshtml</c>).
/// </summary>
/// <param name="Placeholder">The search box's placeholder text.</param>
/// <param name="EngineName">The default engine's name.</param>
/// <param name="Engines">The engines for the search box's menu, in Settings order.</param>
public sealed record SearchPanelModel (string Placeholder, string EngineName, IReadOnlyList<SearchPanelEngine> Engines);

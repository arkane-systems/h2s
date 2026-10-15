namespace h2s.Panels;

/// <summary>
///   The view model for the search panel (<c>Pages/Shared/_SearchPanel.cshtml</c>).
/// </summary>
/// <param name="Placeholder">The search box's placeholder text.</param>
/// <param name="EngineName">The default engine's name.</param>
/// <param name="Shortcuts">The engines' shortcuts with their names, such as <c>!g Google</c>, in order.</param>
public sealed record SearchPanelModel (string Placeholder, string EngineName, IReadOnlyList<string> Shortcuts);

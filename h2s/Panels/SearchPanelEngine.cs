namespace h2s.Panels;

/// <summary>
///   An engine in a search menu: the search box's, or quick launch's when nothing on the dashboard matches.
/// </summary>
/// <param name="Id">The engine's ID, which <c>/Search</c> takes as <c>engine</c> when it's chosen.</param>
/// <param name="Name">The engine's name.</param>
/// <param name="Shortcut">The engine's shortcut, without its <c>!</c>; empty for none.</param>
/// <param name="IsDefault">Whether the engine is the default.</param>
public sealed record SearchPanelEngine (int Id, string Name, string Shortcut, bool IsDefault);

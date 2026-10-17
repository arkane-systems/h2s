using h2s.Widgets;

namespace h2s.Panels;

/// <summary>
///   The view model for a status bar panel (<c>Pages/Shared/_StatusBar.cshtml</c>).
/// </summary>
/// <param name="Title">The bar's title, shown at its start; <c>null</c> for none.</param>
/// <param name="LinkUrl">The page the bar opens when clicked, such as a status page; <c>null</c> when it isn't a link.</param>
/// <param name="Items">The bar's widgets, in order, each shown with its label.</param>
public sealed record StatusBarPanelModel (string? Title, string? LinkUrl, IReadOnlyList<WidgetView> Items);

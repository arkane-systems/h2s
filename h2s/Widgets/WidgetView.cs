using h2s.Widgets.Abstractions;

namespace h2s.Widgets;

/// <summary>
///   A widget as a tile shows it (<c>Pages/Shared/_Widget.cshtml</c>).
/// </summary>
/// <param name="Id">The widget's ID.</param>
/// <param name="LinkId">The link whose tile shows it.</param>
/// <param name="Role">Where it sits on the tile.</param>
/// <param name="SortOrder">Its position among its link's widgets.</param>
/// <param name="RefreshSeconds">How often the page reads it again, in seconds.</param>
/// <param name="Result">What it shows, or <c>null</c> when it hasn't been read recently and the page should read it.</param>
public sealed record WidgetView (int Id, int LinkId, WidgetRole Role, int SortOrder, int RefreshSeconds, WidgetResult? Result);

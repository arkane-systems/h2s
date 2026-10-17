using h2s.Widgets.Abstractions;

namespace h2s.Widgets;

/// <summary>
///   A widget as a tile shows it (<c>Pages/Shared/_Widget.cshtml</c>).
/// </summary>
/// <param name="Id">The widget's ID; 0 for one that isn't saved.</param>
/// <param name="LinkId">The link whose tile shows it; 0 for a panel's widget.</param>
/// <param name="Role">Where it sits on the tile.</param>
/// <param name="SortOrder">Its position among its link's widgets.</param>
/// <param name="RefreshSeconds">How often the page reads it again, in seconds.</param>
/// <param name="Result">What it shows, or <c>null</c> when it hasn't been read recently and the page should read it.</param>
public sealed record WidgetView (int Id, int LinkId, WidgetRole Role, int SortOrder, int RefreshSeconds, WidgetResult? Result)
{
  /// <summary>
  ///   Gets the widget's position in the list the link dialog's preview was rendered from, when it's shown there. A
  ///   preview's widgets are read through the Editor's WidgetPreview handler by that position, rather than by ID, and
  ///   aren't refreshed on an interval.
  /// </summary>
  public int? PreviewIndex { get; init; }

  /// <summary>
  ///   Gets the widget's label, such as a status bar item's name; <c>null</c> for none.
  /// </summary>
  public string? Label { get; init; }
}

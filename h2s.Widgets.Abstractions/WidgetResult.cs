namespace h2s.Widgets.Abstractions;

/// <summary>
///   What a widget shows: an overall status, a few figures, and a sentence describing them. Kinds never write HTML; the
///   core draws every widget the same way.
/// </summary>
/// <param name="Status">How the widget's subject is doing.</param>
/// <param name="Stats">The figures, in the order shown; status widgets show only a few.</param>
/// <param name="Description">
///   A sentence saying what the widget shows, such as "Jellyfin is up: 42 ms, 99.9% over 1 day." It's the widget's
///   tooltip and what screen readers announce.
/// </param>
public sealed record WidgetResult (WidgetStatus Status, IReadOnlyList<WidgetStat> Stats, string Description)
{
  /// <summary>
  ///   Creates the result of a widget that couldn't be read.
  /// </summary>
  /// <param name="description">Why, in a sentence that says how to fix it where that's possible.</param>
  /// <returns>The result.</returns>
  public static WidgetResult Unknown (string description)
    => new (Status: WidgetStatus.Unknown, Stats: [], Description: description);
}

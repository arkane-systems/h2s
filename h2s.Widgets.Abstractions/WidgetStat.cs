namespace h2s.Widgets.Abstractions;

/// <summary>
///   One figure a widget shows, such as a response time.
/// </summary>
/// <param name="Label">What the figure is, such as "Response time". Status widgets show it only to screen readers and in the tooltip.</param>
/// <param name="Value">The figure, formatted, such as <c>42</c>.</param>
/// <param name="Unit">Its unit, such as <c>ms</c>, if it has one.</param>
public sealed record WidgetStat (string Label, string Value, string? Unit = null)
{
  /// <summary>
  ///   Gets how to read the figure, when it deserves a colour of its own (a certificate about to expire, say).
  /// </summary>
  public WidgetStatus? Severity { get; init; }

  /// <summary>
  ///   Gets the figure with its unit, such as <c>42 ms</c>, or <c>99.9%</c> for a percentage.
  /// </summary>
  public string Text
    => this.Unit switch
       {
         null or "" => this.Value,
         "%"        => $"{this.Value}%",
         { } unit   => $"{this.Value} {unit}",
       };
}

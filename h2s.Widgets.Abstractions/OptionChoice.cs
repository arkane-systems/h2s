namespace h2s.Widgets.Abstractions;

/// <summary>
///   One of an option's choices: one of a select option's <see cref="SchemaOption.Choices" />, or one an integration's
///   choice list offers.
/// </summary>
/// <param name="Value">The value stored when it's chosen.</param>
/// <param name="Label">What the edit form shows for it.</param>
public sealed record OptionChoice (string Value, string Label)
{
  /// <summary>
  ///   Gets the heading the edit form lists the choice under, if any. Choices with the same group should be adjacent.
  /// </summary>
  public string? Group { get; init; }
}

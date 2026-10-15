namespace h2s.Panels;

/// <summary>
///   One option in a panel kind's schema. The core stores, checks and edits its value, so a kind never ships its own
///   settings UI.
/// </summary>
/// <param name="Key">The option's name in the panel's stored JSON object.</param>
/// <param name="Label">The option's label in the panel dialog.</param>
/// <param name="Type">The type of the option's value.</param>
public sealed record PanelOption (string Key, string Label, PanelOptionType Type)
{
  /// <summary>
  ///   Gets a sentence explaining the option, shown under it in the panel dialog.
  /// </summary>
  public string? HelpText { get; init; }

  /// <summary>
  ///   Gets a value indicating whether a text or integer option must have a value: text that isn't blank, or a number.
  ///   Booleans always have one.
  /// </summary>
  public bool IsRequired { get; init; }

  /// <summary>
  ///   Gets the value used when the panel has none stored, or a stored value doesn't fit the option: a
  ///   <see cref="string" />, <see cref="int" /> or <see cref="bool" /> to match <see cref="Type" />. Without one, text
  ///   reads as an empty string, a boolean as <c>false</c>, and an integer as blank.
  /// </summary>
  public object? DefaultValue { get; init; }

  /// <summary>
  ///   Gets the smallest value an integer option accepts, if it has a lower bound.
  /// </summary>
  public int? Minimum { get; init; }

  /// <summary>
  ///   Gets the largest value an integer option accepts, if it has an upper bound.
  /// </summary>
  public int? Maximum { get; init; }
}

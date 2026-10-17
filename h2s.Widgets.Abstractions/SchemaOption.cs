namespace h2s.Widgets.Abstractions;

/// <summary>
///   One option in a schema: a panel kind's options, or the settings of an integration's connections. The core stores,
///   checks and edits its value, so neither a kind nor an integration ships its own settings UI.
/// </summary>
/// <param name="Key">The option's name in the stored JSON object.</param>
/// <param name="Label">The option's label in the edit form.</param>
/// <param name="Type">The type of the option's value.</param>
public sealed record SchemaOption (string Key, string Label, OptionType Type)
{
  /// <summary>
  ///   Gets a sentence explaining the option, shown under it in the edit form.
  /// </summary>
  public string? HelpText { get; init; }

  /// <summary>
  ///   Gets a value indicating whether the option must have a value: text, a URL or a secret that isn't blank, or a
  ///   number. Booleans always have one.
  /// </summary>
  public bool IsRequired { get; init; }

  /// <summary>
  ///   Gets the value used when none is stored, or a stored value doesn't fit the option: a <see cref="string" />,
  ///   <see cref="int" /> or <see cref="bool" /> to match <see cref="Type" />. Without one, text reads as an empty string, a
  ///   boolean as <c>false</c>, and an integer or URL as blank. Secrets have no default.
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

  /// <summary>
  ///   Gets a regular expression that a text option's value must match in full when it isn't blank, as an HTML
  ///   <c>pattern</c> attribute does. The edit form uses it as one, so it must also be valid in a JavaScript <c>v</c>-flag
  ///   regular expression: escape a hyphen in a character class (<c>[A-Za-z0-9\-]</c>).
  /// </summary>
  public string? Pattern { get; init; }

  /// <summary>
  ///   Gets the values a select option offers, in order. An optional select may also be blank; the edit form labels that
  ///   choice with <see cref="Placeholder" />.
  /// </summary>
  public IReadOnlyList<OptionChoice> Choices { get; init; } = [];

  /// <summary>
  ///   Gets the name of a choice list, which the edit form offers the option's values from: one the integration that
  ///   declares the option publishes through <see cref="IIntegration.GetChoicesAsync" />, read from its service when the
  ///   form opens. The option keeps its <see cref="Type" />, and the form still lets a value the list doesn't hold be typed
  ///   in, so a service that's down or a value it doesn't report doesn't stop anyone. Only widget kinds' text and integer
  ///   options use it so far.
  /// </summary>
  public string? ChoicesFrom { get; init; }

  /// <summary>
  ///   Gets an example value shown in the empty field, such as <c>https://status.example.com</c>.
  /// </summary>
  public string? Placeholder { get; init; }

  /// <summary>
  ///   Gets what a value that doesn't match <see cref="Pattern" /> should be instead, completing "<c>Label</c> may only
  ///   contain…"-style sentences, such as <c>may only contain letters, numbers, and hyphens</c>.
  /// </summary>
  public string? PatternDescription { get; init; }
}

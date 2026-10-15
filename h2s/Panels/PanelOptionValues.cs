using System.Text.Json;

namespace h2s.Panels;

/// <summary>
///   A panel's option values, read from its stored JSON against its kind's schema. Unknown keys are ignored, and missing
///   values, or values of the wrong type, out of range, or blank where the option is required, take the option's default.
/// </summary>
public sealed class PanelOptionValues
{
  private readonly Dictionary<string, PanelOption> options;
  private readonly Dictionary<string, object?>     values;

  /// <summary>
  ///   Initializes a new instance of the <see cref="PanelOptionValues" /> class.
  /// </summary>
  /// <param name="schema">The kind's options.</param>
  /// <param name="json">The panel's stored JSON object. Anything else, including invalid JSON, reads as all defaults.</param>
  public PanelOptionValues (IReadOnlyList<PanelOption> schema, string? json)
  {
    this.options = schema.ToDictionary (keySelector: o => o.Key, comparer: StringComparer.Ordinal);
    this.values = schema.ToDictionary (keySelector: o => o.Key,
                                       elementSelector: o => o.DefaultValue,
                                       comparer: StringComparer.Ordinal);

    JsonElement stored;

    try
    {
      using JsonDocument document = JsonDocument.Parse (string.IsNullOrWhiteSpace (json) ? "{}" : json);
      stored = document.RootElement.Clone ();
    }
    catch (JsonException)
    {
      return;
    }

    if (stored.ValueKind != JsonValueKind.Object)
      return;

    foreach (PanelOption option in schema)
    {
      if (stored.TryGetProperty (propertyName: option.Key, value: out JsonElement value) &&
          PanelOptionValues.TryRead (option: option, element: value, value: out object? read))
        this.values[option.Key] = read;
    }
  }

  /// <summary>
  ///   Gets every option's value, keyed by option name: a <see cref="string" />, <see cref="int" />, <see cref="bool" />
  ///   or <c>null</c>, in schema order. The panel dialog starts from these.
  /// </summary>
  public IReadOnlyDictionary<string, object?> Values => this.values;

  /// <summary>
  ///   Gets a text option's value.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <returns>The stored text, or the default; an empty string when there is neither.</returns>
  public string GetText (string key) => this.Get (key: key, type: PanelOptionType.Text) as string ?? "";

  /// <summary>
  ///   Gets an integer option's value.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <returns>The stored number, or the default; <c>null</c> when the option is blank.</returns>
  public int? GetInteger (string key) => this.Get (key: key, type: PanelOptionType.Integer) as int?;

  /// <summary>
  ///   Gets a boolean option's value.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <returns>The stored value, or the default; <c>false</c> when there is neither.</returns>
  public bool GetBoolean (string key) => this.Get (key: key, type: PanelOptionType.Boolean) as bool? ?? false;

  /// <summary>
  ///   Gets an option's value, checking that the kind declared it with the type asked for.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <param name="type">The type the caller expects.</param>
  /// <returns>The value.</returns>
  /// <exception cref="ArgumentException">The schema has no option of that key and type.</exception>
  private object? Get (string key, PanelOptionType type)
    => this.options.TryGetValue (key: key, value: out PanelOption? option) && (option.Type == type)
         ? this.values[key]
         : throw new ArgumentException (message: $"The panel kind declares no {type} option \"{key}\".",
                                        paramName: nameof (key));

  /// <summary>
  ///   Reads a stored value, if it fits the option.
  /// </summary>
  /// <param name="option">The option.</param>
  /// <param name="element">The stored value.</param>
  /// <param name="value">The value, when it fits.</param>
  /// <returns><c>true</c> when it fits.</returns>
  private static bool TryRead (PanelOption option, JsonElement element, out object? value)
  {
    value = null;

    switch (option.Type)
    {
      case PanelOptionType.Text when (element.ValueKind == JsonValueKind.String) &&
                                     !(option.IsRequired && string.IsNullOrWhiteSpace (element.GetString ())):
        value = element.GetString ();

        return true;

      case PanelOptionType.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
        value = element.GetBoolean ();

        return true;

      case PanelOptionType.Integer when (element.ValueKind == JsonValueKind.Null) && !option.IsRequired:
        return true;

      case PanelOptionType.Integer when (element.ValueKind == JsonValueKind.Number)  &&
                                        element.TryGetInt32 (out int number)         &&
                                        (number >= (option.Minimum ?? int.MinValue)) &&
                                        (number <= (option.Maximum ?? int.MaxValue)):
        value = number;

        return true;

      default:
        return false;
    }
  }
}

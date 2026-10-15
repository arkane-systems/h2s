using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace h2s.Panels;

/// <summary>
///   Checks the option values the panel dialog posts against a kind's schema, and turns them into the JSON a panel
///   stores.
/// </summary>
public static class PanelOptionForm
{
  /// <summary>
  ///   The longest text value an option accepts.
  /// </summary>
  public const int MaxTextLength = 200;

  /// <summary>
  ///   Checks posted option values.
  /// </summary>
  /// <param name="schema">The kind's options.</param>
  /// <param name="posted">
  ///   The posted values, keyed by option name, as the dialog sends them: text as typed, integers as digits or blank, and
  ///   booleans as <c>true</c> or <c>false</c>. Keys the schema doesn't declare are ignored, and a missing boolean is off.
  /// </param>
  /// <param name="json">The values as a JSON object in schema order, when they're valid.</param>
  /// <param name="error">What's wrong with the first value that isn't, when one isn't.</param>
  /// <returns><c>true</c> when every value fits its option.</returns>
  public static bool TryParse (IReadOnlyList<PanelOption>            schema,
                               IReadOnlyDictionary<string, string?>? posted,
                               [NotNullWhen (true)] out  string?     json,
                               [NotNullWhen (false)] out string?     error)
  {
    json  = null;
    error = null;

    var values = new Dictionary<string, object?> (StringComparer.Ordinal);

    foreach (PanelOption option in schema)
    {
      string raw = (posted?.GetValueOrDefault (option.Key) ?? "").Trim ();

      if (!PanelOptionForm.TryParseValue (option: option, raw: raw, value: out object? value, error: out error))
        return false;

      values[option.Key] = value;
    }

    json = JsonSerializer.Serialize (values);

    return true;
  }

  /// <summary>
  ///   Checks one posted value.
  /// </summary>
  /// <param name="option">The option.</param>
  /// <param name="raw">The posted value, trimmed; empty when it's missing.</param>
  /// <param name="value">The value, when it fits.</param>
  /// <param name="error">What's wrong with it, when it doesn't.</param>
  /// <returns><c>true</c> when it fits.</returns>
  private static bool TryParseValue (PanelOption                       option,
                                     string                            raw,
                                     out                       object? value,
                                     [NotNullWhen (false)] out string? error)
  {
    value = null;
    error = null;

    switch (option.Type)
    {
      case PanelOptionType.Boolean:
        value = string.Equals (a: raw, b: "true", comparisonType: StringComparison.OrdinalIgnoreCase);

        return true;

      case PanelOptionType.Text when option.IsRequired    && (raw.Length == 0):
      case PanelOptionType.Integer when option.IsRequired && (raw.Length == 0):
        error = $"{option.Label} is required.";

        return false;

      case PanelOptionType.Text when raw.Length > PanelOptionForm.MaxTextLength:
        error = $"{option.Label} must be at most {PanelOptionForm.MaxTextLength} characters.";

        return false;

      case PanelOptionType.Text:
        value = raw;

        return true;

      case PanelOptionType.Integer when raw.Length == 0:
        return true;

      case PanelOptionType.Integer:
        return PanelOptionForm.TryParseInteger (option: option, raw: raw, value: out value, error: out error);

      default:
        error = $"{option.Label} has a type h2s can't edit.";

        return false;
    }
  }

  /// <summary>
  ///   Checks a posted integer that isn't blank.
  /// </summary>
  /// <param name="option">The option.</param>
  /// <param name="raw">The posted value, trimmed.</param>
  /// <param name="value">The number, when it fits.</param>
  /// <param name="error">What's wrong with it, when it doesn't.</param>
  /// <returns><c>true</c> when it fits.</returns>
  private static bool TryParseInteger (PanelOption                       option,
                                       string                            raw,
                                       out                       object? value,
                                       [NotNullWhen (false)] out string? error)
  {
    value = null;
    error = null;

    if (!int.TryParse (s: raw,
                       style: NumberStyles.AllowLeadingSign,
                       provider: CultureInfo.InvariantCulture,
                       result: out int number))
      error = $"{option.Label} must be a whole number.";
    else if (number < (option.Minimum ?? int.MinValue))
      error = $"{option.Label} must be at least {option.Minimum}.";
    else if (number > (option.Maximum ?? int.MaxValue))
      error = $"{option.Label} must be at most {option.Maximum}.";
    else
      value = number;

    return error == null;
  }
}

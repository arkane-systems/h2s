using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using h2s.Widgets.Abstractions;

namespace h2s.Options;

/// <summary>
///   Checks the option values an edit form posts against a schema, and turns them into the JSON that's stored.
/// </summary>
public static class OptionForm
{
  /// <summary>
  ///   The longest text value an option accepts.
  /// </summary>
  public const int MaxTextLength = 200;

  /// <summary>
  ///   The longest URL an option accepts.
  /// </summary>
  public const int MaxUrlLength = 2048;

  /// <summary>
  ///   Checks posted option values.
  /// </summary>
  /// <param name="schema">The options.</param>
  /// <param name="posted">
  ///   The posted values, keyed by option name, as the form sends them: text and URLs as typed, integers as digits or blank,
  ///   and booleans as <c>true</c> or <c>false</c>. Keys the schema doesn't declare are ignored, and a missing boolean is
  ///   off. Secret options are skipped, since secrets are stored apart from the other values.
  /// </param>
  /// <param name="json">The values as a JSON object in schema order, when they're valid.</param>
  /// <param name="error">What's wrong with the first value that isn't, when one isn't.</param>
  /// <returns><c>true</c> when every value fits its option.</returns>
  public static bool TryParse (IReadOnlyList<SchemaOption>           schema,
                               IReadOnlyDictionary<string, string?>? posted,
                               [NotNullWhen (true)] out  string?     json,
                               [NotNullWhen (false)] out string?     error)
  {
    json  = null;
    error = null;

    var values = new Dictionary<string, object?> (StringComparer.Ordinal);

    foreach (SchemaOption option in schema.Where (o => o.Type != OptionType.Secret))
    {
      string raw = (posted?.GetValueOrDefault (option.Key) ?? "").Trim ();

      if (!OptionForm.TryParseValue (option: option, raw: raw, value: out object? value, error: out error))
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
  private static bool TryParseValue (SchemaOption                      option,
                                     string                            raw,
                                     out                       object? value,
                                     [NotNullWhen (false)] out string? error)
  {
    value = null;
    error = null;

    switch (option.Type)
    {
      case OptionType.Boolean:
        value = string.Equals (a: raw, b: "true", comparisonType: StringComparison.OrdinalIgnoreCase);

        return true;

      case OptionType.Text or OptionType.Url or OptionType.Integer when option.IsRequired && (raw.Length == 0):
        error = $"{option.Label} is required.";

        return false;

      case OptionType.Text when raw.Length > OptionForm.MaxTextLength:
        error = $"{option.Label} must be at most {OptionForm.MaxTextLength} characters.";

        return false;

      case OptionType.Text when !OptionValues.MatchesPattern (option: option, value: raw):
        error = $"{option.Label} {option.PatternDescription ?? "isn't in the expected form"}.";

        return false;

      case OptionType.Text:
        value = raw;

        return true;

      case OptionType.Url when (raw.Length > OptionForm.MaxUrlLength) || ((raw.Length > 0) && !OptionValues.IsWebUrl (raw)):
        error = $"{option.Label} must be an HTTP or HTTPS URL.";

        return false;

      case OptionType.Url:
        value = raw;

        return true;

      case OptionType.Integer when raw.Length == 0:
        return true;

      case OptionType.Integer:
        return OptionForm.TryParseInteger (option: option, raw: raw, value: out value, error: out error);

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
  private static bool TryParseInteger (SchemaOption                      option,
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

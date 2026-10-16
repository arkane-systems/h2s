using System.Text.Json;
using System.Text.RegularExpressions;

namespace h2s.Widgets.Abstractions;

/// <summary>
///   Option values, read from their stored JSON against a schema. Unknown keys are ignored, and missing values, or values
///   of the wrong type, out of range, not matching the option's pattern, or blank where the option is required, take the
///   option's default. Secrets come separately, already decrypted by the core.
/// </summary>
public sealed class OptionValues
{
  // Long enough for any sensible pattern; a pattern that takes longer counts as not matching.
  private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds (100);

  private readonly Dictionary<string, SchemaOption> options;
  private readonly Dictionary<string, string>       secrets;
  private readonly Dictionary<string, object?>      values;

  /// <summary>
  ///   Initializes a new instance of the <see cref="OptionValues" /> class.
  /// </summary>
  /// <param name="schema">The options.</param>
  /// <param name="json">The stored JSON object. Anything else, including invalid JSON, reads as all defaults.</param>
  /// <param name="secrets">
  ///   The decrypted secrets, keyed by option name. Keys that aren't secret options, and blank values, are ignored.
  /// </param>
  public OptionValues (IReadOnlyList<SchemaOption> schema, string? json, IReadOnlyDictionary<string, string>? secrets = null)
  {
    this.options = schema.ToDictionary (keySelector: o => o.Key, comparer: StringComparer.Ordinal);
    this.values = schema.Where (o => o.Type != OptionType.Secret)
                        .ToDictionary (keySelector: o => o.Key,
                                       elementSelector: o => o.DefaultValue,
                                       comparer: StringComparer.Ordinal);
    this.secrets = schema.Where (o => (o.Type == OptionType.Secret) &&
                                      !string.IsNullOrWhiteSpace (secrets?.GetValueOrDefault (o.Key)))
                         .ToDictionary (keySelector: o => o.Key,
                                        elementSelector: o => secrets![o.Key],
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

    foreach (SchemaOption option in schema)
    {
      if ((option.Type != OptionType.Secret)                                             &&
          stored.TryGetProperty (propertyName: option.Key, value: out JsonElement value) &&
          OptionValues.TryRead (option: option, element: value, value: out object? read))
        this.values[option.Key] = read;
    }
  }

  /// <summary>
  ///   Gets every option's value except the secrets, keyed by option name: a <see cref="string" />, <see cref="int" />,
  ///   <see cref="bool" /> or <c>null</c>, in schema order. Edit forms start from these, so secrets are left out.
  /// </summary>
  public IReadOnlyDictionary<string, object?> Values => this.values;

  /// <summary>
  ///   Determines whether a string matches an option's pattern, if it has one.
  /// </summary>
  /// <param name="option">The option.</param>
  /// <param name="value">The string, already trimmed.</param>
  /// <returns><c>true</c> when the option has no pattern, the string is empty, or it matches the pattern in full.</returns>
  public static bool MatchesPattern (SchemaOption option, string value)
  {
    if (string.IsNullOrEmpty (option.Pattern) || (value.Length == 0))
      return true;

    try
    {
      return Regex.IsMatch (input: value,
                            pattern: $"^(?:{option.Pattern})$",
                            options: RegexOptions.CultureInvariant,
                            matchTimeout: OptionValues.PatternTimeout);
    }
    catch (RegexMatchTimeoutException)
    {
      return false;
    }
  }

  /// <summary>
  ///   Determines whether a string is an absolute HTTP or HTTPS URL without credentials, as URL options require.
  /// </summary>
  /// <param name="value">The string, already trimmed.</param>
  /// <returns><c>true</c> when it is.</returns>
  public static bool IsWebUrl (string value)
    => Uri.TryCreate (uriString: value, uriKind: UriKind.Absolute, result: out Uri? uri) &&
       ((uri.Scheme == Uri.UriSchemeHttp) || (uri.Scheme == Uri.UriSchemeHttps))         &&
       string.IsNullOrEmpty (uri.UserInfo);

  /// <summary>
  ///   Gets a text or URL option's value.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <returns>The stored text, or the default; an empty string when there is neither.</returns>
  public string GetText (string key) => this.Get (key: key, OptionType.Text, OptionType.Url) as string ?? "";

  /// <summary>
  ///   Gets an integer option's value.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <returns>The stored number, or the default; <c>null</c> when the option is blank.</returns>
  public int? GetInteger (string key) => this.Get (key: key, OptionType.Integer) as int?;

  /// <summary>
  ///   Gets a boolean option's value.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <returns>The stored value, or the default; <c>false</c> when there is neither.</returns>
  public bool GetBoolean (string key) => this.Get (key: key, OptionType.Boolean) as bool? ?? false;

  /// <summary>
  ///   Gets a secret option's value.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <returns>The secret, or <c>null</c> when it isn't set (or can no longer be decrypted).</returns>
  public string? GetSecret (string key)
  {
    _ = this.Find (key: key, OptionType.Secret);

    return this.secrets.GetValueOrDefault (key);
  }

  /// <summary>
  ///   Reads a stored value, if it fits the option.
  /// </summary>
  /// <param name="option">The option.</param>
  /// <param name="element">The stored value.</param>
  /// <param name="value">The value, when it fits.</param>
  /// <returns><c>true</c> when it fits.</returns>
  private static bool TryRead (SchemaOption option, JsonElement element, out object? value)
  {
    value = null;

    switch (option.Type)
    {
      case OptionType.Text or OptionType.Url when element.ValueKind == JsonValueKind.String:
        string text = element.GetString ()!;

        if ((option.IsRequired && string.IsNullOrWhiteSpace (text))            ||
            !OptionValues.MatchesPattern (option: option, value: text.Trim ()) ||
            ((option.Type == OptionType.Url) && (text.Trim ().Length > 0) && !OptionValues.IsWebUrl (text.Trim ())))
          return false;

        value = text;

        return true;

      case OptionType.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
        value = element.GetBoolean ();

        return true;

      case OptionType.Integer when (element.ValueKind == JsonValueKind.Null) && !option.IsRequired:
        return true;

      case OptionType.Integer when (element.ValueKind == JsonValueKind.Number)  &&
                                   element.TryGetInt32 (out int number)         &&
                                   (number >= (option.Minimum ?? int.MinValue)) &&
                                   (number <= (option.Maximum ?? int.MaxValue)):
        value = number;

        return true;

      default:
        return false;
    }
  }

  /// <summary>
  ///   Gets an option's value, checking that the schema declares it with a type the caller can read.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <param name="types">The types the caller can read.</param>
  /// <returns>The value.</returns>
  private object? Get (string key, params OptionType[] types) => this.values[this.Find (key: key, types: types).Key];

  /// <summary>
  ///   Finds an option, checking that the schema declares it with a type the caller can read.
  /// </summary>
  /// <param name="key">The option's key.</param>
  /// <param name="types">The types the caller can read.</param>
  /// <returns>The option.</returns>
  /// <exception cref="ArgumentException">The schema has no option of that key and type.</exception>
  private SchemaOption Find (string key, params OptionType[] types)
    => this.options.TryGetValue (key: key, value: out SchemaOption? option) && types.Contains (option.Type)
         ? option
         : throw new ArgumentException (message:
                                        $"The schema declares no {string.Join (separator: " or ", values: types)} option \"{key}\".",
                                        paramName: nameof (key));
}

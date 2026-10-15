using System.Text.RegularExpressions;
using h2s.Models;

namespace h2s.Validation;

/// <summary>
///   Checks a search engine's fields, for the Settings handlers and for import.
/// </summary>
public static partial class SearchEngineRules
{
  /// <summary>
  ///   The placeholder in a URL template where the escaped search terms go.
  /// </summary>
  public const string QueryPlaceholder = "{query}";

  /// <summary>
  ///   The message for a shortcut <see cref="TryNormalizeShortcut" /> rejects.
  /// </summary>
  public static string InvalidShortcut
    => $"The shortcut may only use letters, numbers and hyphens, at most {SearchEngine.MaxShortcutLength} of them.";

  /// <summary>
  ///   Checks an engine's name.
  /// </summary>
  /// <param name="name">The name, trimmed.</param>
  /// <returns>What's wrong with it, or <c>null</c> when it's valid.</returns>
  public static string? CheckName (string name)
    => name.Length == 0
         ? "A name is required."
         : name.Length > SearchEngine.MaxNameLength
           ? $"The name must be at most {SearchEngine.MaxNameLength} characters."
           : null;

  /// <summary>
  ///   Checks an engine's URL template: an absolute HTTP or HTTPS URL with <see cref="QueryPlaceholder" /> in it.
  /// </summary>
  /// <param name="template">The template, trimmed.</param>
  /// <returns>What's wrong with it, or <c>null</c> when it's valid.</returns>
  public static string? CheckUrlTemplate (string template)
  {
    if (!template.Contains (value: SearchEngineRules.QueryPlaceholder, comparisonType: StringComparison.Ordinal))
      return $"The search URL must contain {SearchEngineRules.QueryPlaceholder} where the search terms go.";

    string sample = template.Replace (oldValue: SearchEngineRules.QueryPlaceholder,
                                      newValue: "terms",
                                      comparisonType: StringComparison.Ordinal);

    return Uri.TryCreate (uriString: sample, uriKind: UriKind.Absolute, result: out Uri? uri) &&
           ((uri.Scheme == Uri.UriSchemeHttp) || (uri.Scheme == Uri.UriSchemeHttps))
             ? null
             : "The search URL must start with http:// or https://.";
  }

  /// <summary>
  ///   Normalizes a shortcut: trimmed, without a leading <c>!</c>, and lowercase.
  /// </summary>
  /// <param name="value">The shortcut as entered, such as <c>!G</c> or <c>g</c>; blank for none.</param>
  /// <param name="shortcut">The normalized shortcut, such as <c>g</c>; empty for none.</param>
  /// <returns>
  ///   <c>true</c> when it's blank, or letters, digits and hyphens of at most <see cref="SearchEngine.MaxShortcutLength" />
  ///   characters.
  /// </returns>
  public static bool TryNormalizeShortcut (string? value, out string shortcut)
  {
    shortcut = (value ?? "").Trim ().TrimStart ('!').ToLowerInvariant ();

    return (shortcut.Length == 0) || SearchEngineRules.ShortcutPattern ().IsMatch (shortcut);
  }

  [GeneratedRegex ("^[a-z0-9-]{1,20}$")]
  private static partial Regex ShortcutPattern ();
}

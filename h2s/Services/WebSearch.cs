using h2s.Models;
using h2s.Validation;

namespace h2s.Services;

/// <summary>
///   Turns a query into a search engine's URL: a <c>!shortcut</c> word at its start or end picks the engine; failing that,
///   the engine chosen in the search box's menu; and failing that, the default.
/// </summary>
public static class WebSearch
{
  /// <summary>
  ///   Picks the engine a query goes to and builds its search URL.
  /// </summary>
  /// <param name="engines">The engines, in order.</param>
  /// <param name="query">The query as typed, such as <c>!g kubernetes ingress</c>.</param>
  /// <param name="engineId">
  ///   The engine chosen in the search box's menu, if any. A shortcut in the query overrides it, and an ID that isn't an
  ///   engine's is ignored.
  /// </param>
  /// <returns>
  ///   The search URL, or <c>null</c> when the query is blank or there are no engines. A shortcut that matches no engine is
  ///   left in the terms, for the default engine (DuckDuckGo, say, has bangs of its own).
  /// </returns>
  public static string? BuildUrl (IReadOnlyList<SearchEngine> engines, string? query, int? engineId = null)
  {
    List<string> words = [.. (query ?? "").Split (separator: (char[]?)null, options: StringSplitOptions.RemoveEmptyEntries)];

    if ((words.Count == 0) || (engines.Count == 0))
      return null;

    SearchEngine? engine     = null;
    int[]         candidates = words.Count == 1 ? [0] : [0, words.Count - 1];

    foreach (int index in candidates)
    {
      if (words[index] is ['!', .. var shortcut] &&
          engines.FirstOrDefault (e => (e.Shortcut.Length > 0) &&
                                       string.Equals (a: e.Shortcut,
                                                      b: shortcut,
                                                      comparisonType: StringComparison.OrdinalIgnoreCase)) is { } match)
      {
        engine = match;
        words.RemoveAt (index);

        break;
      }
    }

    engine ??= engines.FirstOrDefault (e => e.Id == engineId) ?? WebSearch.GetDefault (engines);

    return engine!.UrlTemplate.Replace (oldValue: SearchEngineRules.QueryPlaceholder,
                                        newValue: Uri.EscapeDataString (string.Join (separator: ' ', values: words)),
                                        comparisonType: StringComparison.Ordinal);
  }

  /// <summary>
  ///   Gets the default engine: the one marked default, or else the first.
  /// </summary>
  /// <param name="engines">The engines, in order.</param>
  /// <returns>The default engine, or <c>null</c> when there are none.</returns>
  public static SearchEngine? GetDefault (IReadOnlyList<SearchEngine> engines)
    => engines.FirstOrDefault (e => e.IsDefault) ?? engines.FirstOrDefault ();
}

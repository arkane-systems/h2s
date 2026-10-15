using h2s.Models;

namespace h2s.Services;

/// <summary>
///   The search engines a dashboard starts with: Bing (the default, as the search box always used), DuckDuckGo and
///   Google. The AddSearchEngines migration creates the same three, and importing a file from before search engines
///   gives the dashboard these.
/// </summary>
public static class DefaultSearchEngines
{
  /// <summary>
  ///   Creates the default engines.
  /// </summary>
  /// <returns>New, unsaved engines.</returns>
  public static List<SearchEngine> Create ()
    =>
    [
      new ()
      {
        Name        = "Bing",
        UrlTemplate = "https://www.bing.com/search?q={query}",
        Shortcut    = "b",
        IsDefault   = true,
        SortOrder   = 0,
      },
      new () { Name = "DuckDuckGo", UrlTemplate = "https://duckduckgo.com/?q={query}", Shortcut       = "d", SortOrder = 1 },
      new () { Name = "Google", UrlTemplate     = "https://www.google.com/search?q={query}", Shortcut = "g", SortOrder = 2 },
    ];
}

using h2s.Models;
using h2s.Services;
using h2s.Validation;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="WebSearch" /> and <see cref="SearchEngineRules" />.
/// </summary>
public class WebSearchTests
{
  /// <summary>
  ///   The default engines, with Google rather than Bing as the default.
  /// </summary>
  private static readonly List<SearchEngine> Engines =
  [
    .. DefaultSearchEngines.Create ()
                           .Select (e =>
                                    {
                                      e.IsDefault = e.Name == "Google";

                                      return e;
                                    }),
  ];

  [Theory]
  [InlineData ("kubernetes ingress",    "https://www.google.com/search?q=kubernetes%20ingress")]
  [InlineData ("!d kubernetes ingress", "https://duckduckgo.com/?q=kubernetes%20ingress")]
  [InlineData ("kubernetes ingress !B", "https://www.bing.com/search?q=kubernetes%20ingress")]
  [InlineData ("  !b   a&b  ",          "https://www.bing.com/search?q=a%26b")]
  [InlineData ("!d",                    "https://duckduckgo.com/?q=")]
  [InlineData ("!w wikipedia",          "https://www.google.com/search?q=%21w%20wikipedia")]
  [InlineData ("a !d b",                "https://www.google.com/search?q=a%20%21d%20b")]
  public void BuildUrl_PicksTheEngineByShortcutOrDefault (string query, string expected)
    => Assert.Equal (expected: expected, actual: WebSearch.BuildUrl (engines: WebSearchTests.Engines, query: query));

  [Theory]
  [InlineData (null)]
  [InlineData ("")]
  [InlineData ("   ")]
  public void BuildUrl_IsNullForABlankQuery (string? query)
    => Assert.Null (WebSearch.BuildUrl (engines: WebSearchTests.Engines, query: query));

  [Fact]
  public void BuildUrl_IsNullWithoutEngines () => Assert.Null (WebSearch.BuildUrl (engines: [], query: "anything"));

  [Fact]
  public void GetDefault_FallsBackToTheFirstEngine ()
  {
    List<SearchEngine> engines = DefaultSearchEngines.Create ();
    engines[0].IsDefault = false;

    Assert.Equal (expected: "Bing", actual: WebSearch.GetDefault (engines)!.Name);
  }

  [Theory]
  [InlineData ("https://example.com/search?q={query}", null)]
  [InlineData ("http://search.home.arpa/{query}",      null)]
  [InlineData ("https://example.com/search?q=",        "The search URL must contain {query} where the search terms go.")]
  [InlineData ("example.com/?q={query}",               "The search URL must start with http:// or https://.")]
  [InlineData ("javascript:alert({query})",            "The search URL must start with http:// or https://.")]
  public void CheckUrlTemplate_RequiresAWebUrlWithThePlaceholder (string template, string? expected)
    => Assert.Equal (expected: expected, actual: SearchEngineRules.CheckUrlTemplate (template));

  [Theory]
  [InlineData ("",                      true,  "")]
  [InlineData (" !GH ",                 true,  "gh")]
  [InlineData ("ddg-lite",              true,  "ddg-lite")]
  [InlineData ("two words",             false, "two words")]
  [InlineData ("!",                     true,  "")]
  [InlineData ("abcdefghijklmnopqrstu", false, "abcdefghijklmnopqrstu")]
  public void TryNormalizeShortcut_TakesLettersDigitsAndHyphens (string value, bool valid, string expected)
  {
    Assert.Equal (expected: valid,    actual: SearchEngineRules.TryNormalizeShortcut (value: value, shortcut: out string shortcut));
    Assert.Equal (expected: expected, actual: shortcut);
  }
}

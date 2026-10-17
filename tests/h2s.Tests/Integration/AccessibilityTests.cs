using System.Text.RegularExpressions;
using h2s.Tests.Infrastructure;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the accessibility markup the keyboard and screen reader pass added. Behavior in script (focus return, Space
///   on tiles, reduced motion) is checked in the browser.
/// </summary>
public partial class AccessibilityTests
{
  [Theory]
  [InlineData ("/")]
  [InlineData ("/?edit=true")]
  [InlineData ("/Admin/Settings")]
  [InlineData ("/Admin/Editor")]
  public async Task EveryPage_HasASkipLinkToMain (string path)
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: path, cancellationToken: TestContext.Current.CancellationToken);

    // The layout's scoped CSS adds a b-… attribute to its elements, so match attributes in any order.
    Assert.Matches (expectedRegexPattern: """<a [^>]*class="skip-link visually-hidden-focusable" href="#main-content""",
                    actualString: html);
    Assert.Matches (expectedRegexPattern: """<main [^>]*id="main-content" tabindex="-1""", actualString: html);
    Assert.Contains (expectedSubstring: """aria-controls="navbar-content""", actualString: html);
    Assert.Contains (expectedSubstring: """id="navbar-content""",            actualString: html);
    Assert.Single (AccessibilityTests.HeadingOnePattern ().Matches (html));
  }

  [Fact]
  public async Task Dashboard_NamesItsCategoriesAndKeepsIconsDecorative ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    // Each category region is named by its heading.
    foreach (Match region in AccessibilityTests.CategoryRegionPattern ().Matches (html))
    {
      Assert.Contains (expectedSubstring: $"id=\"category-title-{region.Groups["id"].Value}\"", actualString: html);
    }

    Assert.Equal (expected: 5, actual: AccessibilityTests.CategoryRegionPattern ().Count (html));
    Assert.Contains (expectedSubstring: """<h1 class="visually-hidden">Development</h1>""", actualString: html);
    Assert.DoesNotMatch (expectedRegexPattern: """class="link-block-icon"[^>]*alt="[^"]+""", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "aria-label=\"External site\"", actualString: html);
    Assert.Contains (expectedSubstring: """<span class="visually-hidden"> links</span>""", actualString: html);
    Assert.Contains (expectedSubstring: """aria-live="polite""",                           actualString: html);
  }

  [GeneratedRegex ("<h1[ >]")]
  private static partial Regex HeadingOnePattern ();

  [GeneratedRegex ("""data-category-id="(?<id>\d+)"\s+data-admin-category="[^"]+"\s+aria-labelledby="category-title-\k<id>""")]
  private static partial Regex CategoryRegionPattern ();
}

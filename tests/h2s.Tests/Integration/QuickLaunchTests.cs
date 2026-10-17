using System.Text.RegularExpressions;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the quick launch overlay's markup. Its behavior is script, checked in the browser.
/// </summary>
public partial class QuickLaunchTests
{
  [Fact]
  public async Task Index_RendersQuickLaunchWithTheEnginesInOrder ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      await context.SearchEngines.Where (e => e.Name == "Google")
                   .ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression: e => e.SortOrder,
                                                                              valueExpression: -1),
                                        cancellationToken: TestContext.Current.CancellationToken);
    }

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """<div id="quick-launch" class="quick-launch" hidden data-search-url="/Search">""",
                     actualString: html);
    Assert.Equal (expected: [("Google", "false"), ("Bing", "true"), ("DuckDuckGo", "false")],
                  actual: QuickLaunchTests.EnginePattern ()
                                          .Matches (html)
                                          .Select (m => (m.Groups["name"].Value, m.Groups["default"].Value)));
    Assert.Matches (expectedRegexPattern: """<script type="module" src="/js/modules/quick-launch\.[^"]*js""", actualString: html);
  }

  [Fact]
  public async Task Index_EditModeHasNoQuickLaunch ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/?edit=true",
                                                cancellationToken: TestContext.Current.CancellationToken);

    // The import map lists every module, so look for a script tag rather than the module's name.
    Assert.DoesNotContain (expectedSubstring: "id=\"quick-launch\"", actualString: html);
    Assert.DoesNotMatch (expectedRegexPattern: """<script type="module" src="/js/modules/quick-launch""", actualString: html);
  }

  [Fact]
  public async Task Index_QuickLaunchOffersNoEnginesWhenThereAreNone ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      await context.SearchEngines.ExecuteDeleteAsync (TestContext.Current.CancellationToken);
    }

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "id=\"quick-launch\"", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "quick-launch-engines", actualString: html);
  }

  [GeneratedRegex (pattern:
                   """class="quick-launch-engine"[^>]*data-engine-name="(?<name>[^"]+)"[^>]*data-default="(?<default>[^"]+)""",
                   options: RegexOptions.Singleline)]
  private static partial Regex EnginePattern ();
}

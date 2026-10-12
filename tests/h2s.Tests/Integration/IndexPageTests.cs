using System.Text.RegularExpressions;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the public dashboard page.
/// </summary>
public partial class IndexPageTests
{
  [Fact]
  public async Task Index_FlagsExternalLinks ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    // The seeded Reference category holds the three links outside home.arpa.
    Assert.Equal (expected: 3, actual: IndexPageTests.ExternalFlagPattern ().Count (html));
  }

  [Fact]
  public async Task Index_HidesKumaFeaturesWhenUnconfigured ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.DoesNotContain (expectedSubstring: "uptime-status-bar", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "link-status-badge", actualString: html);
  }

  [Fact]
  public async Task Index_ListsRegularCategoriesBeforeAdminCategories ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);
    IEnumerable<string> names = IndexPageTests.CategoryHeadingPattern ().Matches (html).Select (m => m.Groups["name"].Value);

    Assert.Equal (expected: ["Home", "Media", "Reference", "Infrastructure", "Network"], actual: names);
    Assert.Contains (expectedSubstring: "category-grid-break", actualString: html);
  }

  [Fact]
  public async Task Index_RendersKumaBadgesWhenConfigured ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      await context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls: s => s
                                                                                .SetProperty (propertyExpression:
                                                                                              x => x.UptimeKumaServerUrl,
                                                                                              valueExpression:
                                                                                              "https://status.home.arpa")
                                                                                .SetProperty (propertyExpression:
                                                                                              x => x.UptimeKumaStatusPageSlug,
                                                                                              valueExpression: "main"),
                                                          cancellationToken: TestContext.Current.CancellationToken);
    }

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """href="https://status.home.arpa/status/main""",                     actualString: html);
    Assert.Contains (expectedSubstring: "https://status.home.arpa/api/badge/1/uptime/24?style=flat",          actualString: html);
    Assert.Contains (expectedSubstring: "https://status.home.arpa/api/badge/11/status?style=flat&amp;label=", actualString: html);
  }

  [Fact]
  public async Task Index_UsesSettingsForTitle ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "<title>h2s - Development</title>", actualString: html);
    Assert.Contains (expectedSubstring: "Seeded sample data",               actualString: html);
  }

  [GeneratedRegex ("""<h2 class="h5 mb-0">(?<name>[^<]+)</h2>""")]
  private static partial Regex CategoryHeadingPattern ();

  [GeneratedRegex ("""class="link-external-flag""")]
  private static partial Regex ExternalFlagPattern ();
}

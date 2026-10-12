using System.Text.RegularExpressions;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
/// Tests for the public dashboard page.
/// </summary>
public partial class IndexPageTests
{
  [Fact]
  public async Task Index_FlagsExternalLinks ()
  {
    await using var factory = new DashboardApplicationFactory ();

    var html = await factory.CreateClient ().GetStringAsync ("/", TestContext.Current.CancellationToken);

    // The seeded Reference category holds the three links outside home.arpa.
    Assert.Equal (3, IndexPageTests.ExternalFlagPattern ().Count (html));
  }

  [Fact]
  public async Task Index_HidesKumaFeaturesWhenUnconfigured ()
  {
    await using var factory = new DashboardApplicationFactory ();

    var html = await factory.CreateClient ().GetStringAsync ("/", TestContext.Current.CancellationToken);

    Assert.DoesNotContain ("uptime-status-bar", html);
    Assert.DoesNotContain ("link-status-badge", html);
  }

  [Fact]
  public async Task Index_ListsRegularCategoriesBeforeAdminCategories ()
  {
    await using var factory = new DashboardApplicationFactory ();

    var html = await factory.CreateClient ().GetStringAsync ("/", TestContext.Current.CancellationToken);
    var names = IndexPageTests.CategoryHeadingPattern ().Matches (html).Select (m => m.Groups["name"].Value);

    Assert.Equal (["Home", "Media", "Reference", "Infrastructure", "Network"], names);
    Assert.Contains ("category-grid-break", html);
  }

  [Fact]
  public async Task Index_RendersKumaBadgesWhenConfigured ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context)    = factory.CreateContext ();

    using (scope)
    {
      await context.DashboardSettings.ExecuteUpdateAsync (s => s.SetProperty (x => x.UptimeKumaServerUrl, "https://status.home.arpa")
                                                                .SetProperty (x => x.UptimeKumaStatusPageSlug, "main"),
                                                          TestContext.Current.CancellationToken);
    }

    var html = await factory.CreateClient ().GetStringAsync ("/", TestContext.Current.CancellationToken);

    Assert.Contains ("""href="https://status.home.arpa/status/main""", html);
    Assert.Contains ("https://status.home.arpa/api/badge/1/uptime/24?style=flat", html);
    Assert.Contains ("https://status.home.arpa/api/badge/11/status?style=flat&amp;label=", html);
  }

  [Fact]
  public async Task Index_UsesSettingsForTitle ()
  {
    await using var factory = new DashboardApplicationFactory ();

    var html = await factory.CreateClient ().GetStringAsync ("/", TestContext.Current.CancellationToken);

    Assert.Contains ("<title>h2s - Development</title>", html);
    Assert.Contains ("Seeded sample data", html);
  }

  [GeneratedRegex ("""<h2 class="h5 mb-0">(?<name>[^<]+)</h2>""")]
  private static partial Regex CategoryHeadingPattern ();

  [GeneratedRegex ("""class="link-external-flag""")]
  private static partial Regex ExternalFlagPattern ();
}

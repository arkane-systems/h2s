using h2s.Pages;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Unit;

/// <summary>
/// Tests for the URL helpers on <see cref="IndexModel"/>.
/// </summary>
public class IndexModelTests
{
  [Theory]
  [InlineData ("https://jellyfin.home.arpa/", false)]
  [InlineData ("https://home.arpa/", false)]
  [InlineData ("https://JELLYFIN.HOME.ARPA/", false)]
  [InlineData ("https://wiki.corp.example.com/page", false)]
  [InlineData ("https://github.com/", true)]
  [InlineData ("https://evilhome.arpa/", true)]
  [InlineData ("https://example.com/", true)]
  [InlineData ("not a url", false)]
  public async Task IsExternalLink_UsesLocalDomains (string url, bool expected)
  {
    var model = await IndexModelTests.LoadAsync (localDomains: "https://Home.Arpa/, .corp.example.com");

    Assert.Equal (expected, model.IsExternalLink (url));
  }

  [Fact]
  public async Task IsExternalLink_IsFalseWithoutLocalDomains ()
  {
    var model = await IndexModelTests.LoadAsync (localDomains: "");

    Assert.False (model.IsExternalLink ("https://github.com/"));
  }

  [Fact]
  public async Task BuildBadgeUrl_BuildsKumaUrls ()
  {
    var model = await IndexModelTests.LoadAsync (localDomains: "", kumaServerUrl: "https://status.home.arpa/");

    Assert.Equal ("https://status.home.arpa/api/badge/7/status?style=flat", model.BuildBadgeUrl ("7", "status"));
    Assert.Equal ("https://status.home.arpa/api/badge/7/uptime/24?style=flat", model.BuildBadgeUrl ("7", "uptime", duration: 24));
    Assert.Equal ("https://status.home.arpa/api/badge/7/status?style=flat&label=", model.BuildBadgeUrl ("7", "status", label: ""));
    Assert.Equal ("", model.BuildBadgeUrl ("", "status"));
  }

  [Fact]
  public async Task BuildStatusPageUrl_RequiresSlug ()
  {
    var withoutSlug = await IndexModelTests.LoadAsync (localDomains: "", kumaServerUrl: "https://status.home.arpa");
    var withSlug    = await IndexModelTests.LoadAsync (localDomains: "", kumaServerUrl: "https://status.home.arpa/", kumaSlug: "main");

    Assert.Equal ("", withoutSlug.BuildStatusPageUrl ());
    Assert.Equal ("https://status.home.arpa/status/main", withSlug.BuildStatusPageUrl ());
  }

  /// <summary>
  /// Creates an <see cref="IndexModel"/> over a fresh database with the given settings and runs its GET handler.
  /// </summary>
  /// <param name="localDomains">The local domains setting.</param>
  /// <param name="kumaServerUrl">The Uptime Kuma server URL setting.</param>
  /// <param name="kumaSlug">The Uptime Kuma status page slug setting.</param>
  /// <returns>The loaded page model.</returns>
  private static async Task<IndexModel> LoadAsync (string localDomains, string? kumaServerUrl = null, string? kumaSlug = null)
  {
    await using var database = await TestDatabase.CreateAsync ();
    await using var context  = database.CreateContext ();

    await context.DashboardSettings.ExecuteUpdateAsync (s => s.SetProperty (x => x.LocalDomains, localDomains)
                                                              .SetProperty (x => x.UptimeKumaServerUrl, kumaServerUrl)
                                                              .SetProperty (x => x.UptimeKumaStatusPageSlug, kumaSlug),
                                                        TestContext.Current.CancellationToken);

    var model = new IndexModel (context, new DashboardSettingsService (context));
    await model.OnGetAsync ();

    return model;
  }
}

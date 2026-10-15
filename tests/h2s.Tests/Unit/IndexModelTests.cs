using System.Security.Claims;
using h2s.Data;
using h2s.Models;
using h2s.Pages;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for the URL helpers on <see cref="IndexModel" />.
/// </summary>
public class IndexModelTests
{
  [Theory]
  [InlineData ("https://jellyfin.home.arpa/",        false)]
  [InlineData ("https://home.arpa/",                 false)]
  [InlineData ("https://JELLYFIN.HOME.ARPA/",        false)]
  [InlineData ("https://wiki.corp.example.com/page", false)]
  [InlineData ("https://github.com/",                true)]
  [InlineData ("https://evilhome.arpa/",             true)]
  [InlineData ("https://example.com/",               true)]
  [InlineData ("not a url",                          false)]
  public async Task IsExternalLink_UsesLocalDomains (string url, bool expected)
  {
    IndexModel model = await IndexModelTests.LoadAsync (localDomains: "https://Home.Arpa/, .corp.example.com");

    Assert.Equal (expected: expected, actual: model.IsExternalLink (url));
  }

  [Theory]
  [InlineData ("https://status.home.arpa/", 11,   "https://status.home.arpa/api/badge/11/status?style=flat&label=")]
  [InlineData ("https://status.home.arpa/", null, null)]
  [InlineData (null,                        11,   null)]
  public async Task CreateLinkTile_ShowsBadgeOnlyForMonitoredLinksWithKuma (string? kumaServerUrl,
                                                                            int?    monitorId,
                                                                            string? expectedBadgeUrl)
  {
    IndexModel model = await IndexModelTests.LoadAsync (localDomains: "home.arpa", kumaServerUrl: kumaServerUrl);

    LinkTile tile =
      model.CreateLinkTile (new Link { Label = "Jellyfin", Url = "https://jellyfin.home.arpa/", MonitorId = monitorId });

    Assert.Equal (expected: expectedBadgeUrl, actual: tile.StatusBadgeUrl);
    Assert.False (tile.IsExternal);
  }

  [Fact]
  public async Task CreateLinkTile_FlagsExternalLinks ()
  {
    IndexModel model = await IndexModelTests.LoadAsync (localDomains: "home.arpa");

    Assert.True (model.CreateLinkTile (new Link { Label = "GitHub", Url = "https://github.com/" }).IsExternal);
  }

  [Fact]
  public async Task IsExternalLink_IsFalseWithoutLocalDomains ()
  {
    IndexModel model = await IndexModelTests.LoadAsync (localDomains: "");

    Assert.False (model.IsExternalLink ("https://github.com/"));
  }

  [Fact]
  public async Task BuildBadgeUrl_BuildsKumaUrls ()
  {
    IndexModel model = await IndexModelTests.LoadAsync (localDomains: "", kumaServerUrl: "https://status.home.arpa/");

    Assert.Equal (expected: "https://status.home.arpa/api/badge/7/status?style=flat",
                  actual: model.BuildBadgeUrl (monitorId: 7, type: "status"));
    Assert.Equal (expected: "https://status.home.arpa/api/badge/7/uptime/24?style=flat",
                  actual: model.BuildBadgeUrl (monitorId: 7, type: "uptime", duration: 24));
    Assert.Equal (expected: "https://status.home.arpa/api/badge/7/status?style=flat&label=",
                  actual: model.BuildBadgeUrl (monitorId: 7, type: "status", label: ""));
  }

  [Fact]
  public async Task BuildBadgeUrl_IsEmptyWithoutKumaServer ()
    => Assert.Equal (expected: "",
                     actual: (await IndexModelTests.LoadAsync (localDomains: "")).BuildBadgeUrl (monitorId: 7, type: "status"));

  [Fact]
  public async Task BuildStatusPageUrl_RequiresSlug ()
  {
    IndexModel withoutSlug = await IndexModelTests.LoadAsync (localDomains: "", kumaServerUrl: "https://status.home.arpa");
    IndexModel withSlug =
      await IndexModelTests.LoadAsync (localDomains: "", kumaServerUrl: "https://status.home.arpa/", kumaSlug: "main");

    Assert.Equal (expected: "",                                     actual: withoutSlug.BuildStatusPageUrl ());
    Assert.Equal (expected: "https://status.home.arpa/status/main", actual: withSlug.BuildStatusPageUrl ());
  }

  /// <summary>
  ///   Creates an <see cref="IndexModel" /> over a fresh database with the given settings and runs its GET handler.
  /// </summary>
  /// <param name="localDomains">The local domains setting.</param>
  /// <param name="kumaServerUrl">The Uptime Kuma server URL setting.</param>
  /// <param name="kumaSlug">The Uptime Kuma status page slug setting.</param>
  /// <returns>The loaded page model.</returns>
  private static async Task<IndexModel> LoadAsync (string localDomains, string? kumaServerUrl = null, string? kumaSlug = null)
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();

    await context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls: s => s
                                                                              .SetProperty (propertyExpression: x => x.LocalDomains,
                                                                                            valueExpression: localDomains)
                                                                              .SetProperty (propertyExpression:
                                                                                            x => x.UptimeKumaServerUrl,
                                                                                            valueExpression: kumaServerUrl)
                                                                              .SetProperty (propertyExpression:
                                                                                            x => x.UptimeKumaStatusPageSlug,
                                                                                            valueExpression: kumaSlug),
                                                        cancellationToken: TestContext.Current.CancellationToken);

    using var cache = new MemoryCache (new MemoryCacheOptions ());
    var model = new IndexModel (context: context,
                                settingsService: new DashboardSettingsService (context: context, cache: cache),
                                authorizationService: new AllowAllAuthorizationService ());
    await model.OnGetAsync ();

    return model;
  }

  /// <summary>
  ///   An authorization service that grants everything. These tests never request edit mode, the only thing it guards.
  /// </summary>
  private sealed class AllowAllAuthorizationService : IAuthorizationService
  {
    /// <inheritdoc />
    public Task<AuthorizationResult> AuthorizeAsync (ClaimsPrincipal                        user,
                                                     object?                                resource,
                                                     IEnumerable<IAuthorizationRequirement> requirements)
      => Task.FromResult (AuthorizationResult.Success ());

    /// <inheritdoc />
    public Task<AuthorizationResult> AuthorizeAsync (ClaimsPrincipal user, object? resource, string policyName)
      => Task.FromResult (AuthorizationResult.Success ());
  }
}

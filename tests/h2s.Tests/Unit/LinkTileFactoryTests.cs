using h2s.Models;
using h2s.Pages;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="LinkTileFactory" />'s Uptime Kuma badge URLs.
/// </summary>
public class LinkTileFactoryTests
{
  [Fact]
  public void BuildBadgeUrl_BuildsKumaUrls ()
  {
    var tiles = new LinkTileFactory (new DashboardSettings { UptimeKumaServerUrl = "https://status.home.arpa/" });

    Assert.Equal (expected: "https://status.home.arpa/api/badge/7/status?style=flat",
                  actual: tiles.BuildBadgeUrl (monitorId: 7, type: "status"));
    Assert.Equal (expected: "https://status.home.arpa/api/badge/7/uptime/24?style=flat",
                  actual: tiles.BuildBadgeUrl (monitorId: 7, type: "uptime", duration: 24));
    Assert.Equal (expected: "https://status.home.arpa/api/badge/7/status?style=flat&label=",
                  actual: tiles.BuildBadgeUrl (monitorId: 7, type: "status", label: ""));
  }

  [Fact]
  public void BuildBadgeUrl_IsEmptyWithoutKumaServer ()
    => Assert.Equal (expected: "",
                     actual: new LinkTileFactory (new DashboardSettings ()).BuildBadgeUrl (monitorId: 7, type: "status"));
}

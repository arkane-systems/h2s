using h2s.Integrations.UptimeKuma;
using h2s.Services;
using h2s.Tests.Infrastructure;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="PrometheusText" />, <see cref="KumaMetrics" /> and <see cref="UptimeKumaWindows" />.
/// </summary>
public class KumaMetricsTests
{
  [Fact]
  public void PrometheusText_ReadsSamples_LabelsAndSpecialValues ()
  {
    List<PrometheusSample> samples = PrometheusText.Parse ("""
                                                           # HELP up Whether it's up
                                                           # TYPE up gauge
                                                           up 1
                                                           temperature{room="hall",note="say \"hi\"\nthen \\ go"} 21.5 1700000000000
                                                           ratio{empty=""} NaN
                                                           big +Inf
                                                           broken{unclosed="x 1
                                                           {no_name="x"} 1
                                                           """);

    Assert.Equal (expected: ["up", "temperature", "ratio", "big"], actual: samples.Select (s => s.Name));
    Assert.Equal (expected: 21.5,                                  actual: samples[1].Value);
    Assert.Equal (expected: "hall",                                actual: samples[1].Labels["room"]);
    Assert.Equal (expected: "say \"hi\"\nthen \\ go",              actual: samples[1].Labels["note"]);
    Assert.Equal (expected: "",                                    actual: samples[2].Labels["empty"]);
    Assert.True (double.IsNaN (samples[2].Value));
    Assert.True (double.IsPositiveInfinity (samples[3].Value));
  }

  [Fact]
  public void KumaMetrics_ReadsEachMonitor ()
  {
    KumaMetrics metrics = KumaMetrics.Parse (TestConnections.KumaMetricsText);

    Assert.Null (metrics.Message);
    Assert.Equal (expected: [1, 11, 12], actual: metrics.Monitors.Keys.Order ());

    KumaMonitorMetrics group = metrics.Monitors[1];

    Assert.True (group.IsGroup);
    Assert.Equal (expected: KumaMonitorStatus.Up, actual: group.Status);
    Assert.Equal (expected: 0.99861,              actual: group.Uptime["1d"]);
    Assert.Equal (expected: 0.9998,               actual: group.Uptime["30d"]);

    KumaMonitorMetrics jellyfin = metrics.Monitors[11];

    Assert.Equal (expected: ("Jellyfin", "http", KumaMonitorStatus.Down, 42d, 439, true),
                  actual: (jellyfin.Name, jellyfin.Type, jellyfin.Status, jellyfin.ResponseTimeMs, jellyfin.CertDaysRemaining,
                           jellyfin.CertIsValid));
    Assert.Equal (expected: "Sonarr \"TV\"", actual: metrics.Monitors[12].Name);
    Assert.Null (metrics.Monitors[12].ResponseTimeMs);
  }

  [Fact]
  public void KumaMetrics_SkipsSamplesWithoutAMonitorIdAndUnknownStatuses ()
  {
    KumaMetrics metrics = KumaMetrics.Parse ("""
                                             process_cpu_seconds_total 12
                                             monitor_status{monitor_name="No ID"} 1
                                             monitor_status{monitor_id="abc"} 1
                                             monitor_status{monitor_id="5",monitor_name=""} 9
                                             """);

    KumaMonitorMetrics monitor = Assert.Single (metrics.Monitors.Values);

    Assert.Equal (expected: (5, "Monitor 5"), actual: (monitor.Id, monitor.Name));
    Assert.Null (monitor.Status);
  }

  [Fact]
  public void ToMonitorList_PutsGroupsFirst_ThenSortsByName ()
  {
    KumaMonitorList list = KumaMetrics.Parse (TestConnections.KumaMetricsText).ToMonitorList ();

    Assert.Equal (expected: ["Groups", "Monitors"], actual: list.Groups.Select (g => g.Name));
    Assert.Equal (expected: [1],                    actual: list.Groups[0].Monitors.Select (m => m.Id));
    Assert.Equal (expected: [11, 12],               actual: list.Groups[1].Monitors.Select (m => m.Id));
  }

  [Fact]
  public void ToMonitorList_CarriesAFailure ()
    => Assert.Equal (expected: "Nope.", actual: KumaMetrics.Failed ("Nope.").ToMonitorList ().Message);

  [Theory]
  [InlineData (1,    "1d")]
  [InlineData (24,   "1d")]
  [InlineData (25,   "30d")]
  [InlineData (720,  "30d")]
  [InlineData (721,  "365d")]
  [InlineData (9000, "365d")]
  public void UptimeKumaWindows_FromHours_TakesTheShortestWindowThatCovers (int hours, string window)
    => Assert.Equal (expected: window, actual: UptimeKumaWindows.FromHours (hours));

  [Theory]
  [InlineData ("1d",    24)]
  [InlineData ("30d",   720)]
  [InlineData ("365d",  8760)]
  [InlineData ("other", 24)]
  public void UptimeKumaWindows_ToHours_GivesTheBadgePeriod (string window, int hours)
    => Assert.Equal (expected: hours, actual: UptimeKumaWindows.ToHours (window));

  [Fact]
  public void UptimeKumaSettings_LeavesTheApiKeyOutOfToString ()
    => Assert.DoesNotContain (expectedSubstring: "secret",
                              actualString: new UptimeKumaSettings (ServerUrl: "https://k", DefaultUptimeWindow: "1d")
                                            {
                                              ApiKey = "secret",
                                            }.ToString ());
}

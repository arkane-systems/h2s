using System.Globalization;
using h2s.Services;
using h2s.Widgets.Abstractions;

namespace h2s.Integrations.UptimeKuma;

/// <summary>
///   An Uptime Kuma monitor's status, as a status pill: whether it's up, and optionally its response time and its uptime
///   over a window. It reads Kuma's <c>/metrics</c>, which <see cref="UptimeKumaClient" /> caches, so a dashboard of these
///   costs Kuma one request per refresh.
/// </summary>
public sealed class KumaMonitorWidgetKind : IWidgetKind
{
  /// <summary>
  ///   The kind's key.
  /// </summary>
  public const string KindKey = "monitor";

  /// <summary>
  ///   The key of the option holding the monitor ID.
  /// </summary>
  public const string MonitorIdOption = "monitorId";

  /// <summary>
  ///   The key of the option that shows the response time.
  /// </summary>
  public const string ShowResponseTimeOption = "showResponseTime";

  /// <summary>
  ///   The key of the option that shows the uptime.
  /// </summary>
  public const string ShowUptimeOption = "showUptime";

  /// <summary>
  ///   The key of the option holding the uptime window; blank uses the connection's default.
  /// </summary>
  public const string UptimeWindowOption = "uptimeWindow";

  /// <summary>
  ///   Formats an uptime ratio as a percentage: two decimals from 99%, where they matter, one below, truncated so nearly
  ///   100% never shows as 100.
  /// </summary>
  /// <param name="ratio">The ratio, from 0 to 1.</param>
  /// <returns>Such as <c>100</c>, <c>99.9</c> or <c>99.99</c>.</returns>
  public static string FormatPercent (double ratio)
  {
    double percent = ratio * 100;

    if (percent >= 100)
    {
      return "100";
    }

    // Truncate, so 99.996% doesn't round up to a misleading 100%.
    // Near 100% the second decimal matters; below 99% one is plenty.
    double shown = percent >= 99 ? Math.Floor (percent * 100) / 100 : Math.Floor (percent * 10) / 10;

    return shown.ToString (format: percent >= 99 ? "0.00" : "0.0", provider: CultureInfo.InvariantCulture);
  }

  /// <inheritdoc />
  public string Key => KumaMonitorWidgetKind.KindKey;

  /// <inheritdoc />
  public string DisplayName => "Monitor status";

  /// <inheritdoc />
  public WidgetRole Role => WidgetRole.Status;

  /// <inheritdoc />
  public TimeSpan DefaultRefresh => TimeSpan.FromSeconds (60);

  /// <inheritdoc />
  public IReadOnlyList<SchemaOption> Options { get; } =
    [
      new (Key: KumaMonitorWidgetKind.MonitorIdOption, Label: "Monitor", Type: OptionType.Integer)
      {
        IsRequired  = true,
        Minimum     = 1,
        ChoicesFrom = UptimeKumaIntegration.MonitorsList,
        HelpText    = "For a monitor Kuma doesn't list, enter its ID, as in its address: /dashboard/12.",
      },
      new (Key: KumaMonitorWidgetKind.ShowResponseTimeOption, Label: "Show response time", Type: OptionType.Boolean)
      {
        DefaultValue = true,
      },
      new (Key: KumaMonitorWidgetKind.ShowUptimeOption, Label: "Show uptime", Type: OptionType.Boolean) { DefaultValue = true },
      new (Key: KumaMonitorWidgetKind.UptimeWindowOption, Label: "Uptime period", Type: OptionType.Select)
      {
        Choices = UptimeKumaWindows.Choices, Placeholder = "The Uptime Kuma default",
      },
    ];

  /// <inheritdoc />
  public async Task<WidgetResult> FetchAsync (WidgetContext context)
  {
    if (UptimeKumaSettings.From (context.Connection) is not { } settings)
    {
      return WidgetResult.Unknown ("Uptime Kuma isn't set up.");
    }

    if (settings.ApiKey == null)
    {
      return WidgetResult.Unknown ("Uptime Kuma has no API key; add one in Settings → Integrations.");
    }

    int monitorId = context.Options.GetInteger (KumaMonitorWidgetKind.MonitorIdOption) ?? 0;

    KumaMetrics metrics = await context.Services.GetRequiredService<UptimeKumaClient> ()
                                       .GetMetricsAsync (serverUrl: settings.ServerUrl,
                                                         apiKey: settings.ApiKey,
                                                         cancellationToken: context.CancellationToken);

    if (metrics.Message != null)
    {
      return WidgetResult.Unknown (metrics.Message);
    }

    if (!metrics.Monitors.TryGetValue (key: monitorId, value: out KumaMonitorMetrics? monitor))
    {
      return WidgetResult.Unknown ($"Uptime Kuma doesn't report a monitor {monitorId}.");
    }

    string window = context.Options.GetText (KumaMonitorWidgetKind.UptimeWindowOption) is { Length: > 0 } chosen
                      ? chosen
                      : settings.DefaultUptimeWindow;
    var stats = new List<WidgetStat> ();

    if (context.Options.GetBoolean (KumaMonitorWidgetKind.ShowResponseTimeOption) && monitor.ResponseTimeMs is { } ping)
    {
      stats.Add (new WidgetStat (Label: "Response time",
                                 Value: ping.ToString (format: "0", provider: CultureInfo.InvariantCulture),
                                 Unit: "ms"));
    }

    if (context.Options.GetBoolean (KumaMonitorWidgetKind.ShowUptimeOption) &&
        monitor.Uptime.TryGetValue (key: window, value: out double ratio))
    {
      stats.Add (new WidgetStat (Label: "Uptime", Value: KumaMonitorWidgetKind.FormatPercent (ratio), Unit: "%"));
    }

    (WidgetStatus status, string state) = monitor.Status switch
                                          {
                                            KumaMonitorStatus.Up          => (WidgetStatus.Ok, "up"),
                                            KumaMonitorStatus.Down        => (WidgetStatus.Error, "down"),
                                            KumaMonitorStatus.Pending     => (WidgetStatus.Warning, "pending"),
                                            KumaMonitorStatus.Maintenance => (WidgetStatus.Inactive, "in maintenance"),
                                            _                             => (WidgetStatus.Unknown, "of unknown status"),
                                          };

    string windowLabel = UptimeKumaWindows.Choices.FirstOrDefault (c => c.Value == window)?.Label ?? window;
    string details = string.Join (separator: ", ",
                                  values: stats.Select (s => s.Label == "Uptime" ? $"{s.Text} uptime over {windowLabel}" : s.Text));

    return new WidgetResult (Status: status,
                             Stats: stats,
                             Description: details.Length == 0
                                            ? $"{monitor.Name} is {state}."
                                            : $"{monitor.Name} is {state}: {details}.");
  }
}

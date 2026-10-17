namespace h2s.Services;

/// <summary>
///   Every monitor Uptime Kuma's <c>/metrics</c> endpoint reports, or why it couldn't be read.
/// </summary>
/// <param name="Monitors">The monitors, keyed by ID; empty when they couldn't be read.</param>
/// <param name="Message">Why the metrics couldn't be read, or <c>null</c> when they were.</param>
public sealed record KumaMetrics (IReadOnlyDictionary<int, KumaMonitorMetrics> Monitors, string? Message)
{
  /// <summary>
  ///   Creates metrics that couldn't be read.
  /// </summary>
  /// <param name="message">Why.</param>
  /// <returns>The metrics.</returns>
  public static KumaMetrics Failed (string message) => new (Monitors: new Dictionary<int, KumaMonitorMetrics> (), Message: message);

  /// <summary>
  ///   Reads Kuma's metrics from a Prometheus text document. Samples without a usable <c>monitor_id</c> are skipped.
  /// </summary>
  /// <param name="text">The document.</param>
  /// <returns>The monitors.</returns>
  public static KumaMetrics Parse (string text)
  {
    var monitors = new Dictionary<int, KumaMonitorMetrics> ();

    foreach (PrometheusSample sample in PrometheusText.Parse (text))
    {
      if (!sample.Name.StartsWith (value: "monitor_", comparisonType: StringComparison.Ordinal) ||
          !sample.Labels.TryGetValue (key: "monitor_id", value: out string? idText)             ||
          !int.TryParse (s: idText, result: out int id)                                         ||
          (id <= 0))
      {
        continue;
      }

      KumaMonitorMetrics monitor = monitors.GetValueOrDefault (id) ??
                                   new KumaMonitorMetrics (Id: id,
                                                           Name: sample.Labels.GetValueOrDefault ("monitor_name") is
                                                                 {
                                                                   Length: > 0,
                                                                 } name
                                                                   ? name
                                                                   : $"Monitor {id}",
                                                           Type: sample.Labels.GetValueOrDefault ("monitor_type") ?? "",
                                                           Status: null,
                                                           ResponseTimeMs: null,
                                                           Uptime: new Dictionary<string, double> (StringComparer.Ordinal),
                                                           CertDaysRemaining: null,
                                                           CertIsValid: null);

      monitors[id] = sample.Name switch
                     {
                       "monitor_status" when Enum.IsDefined ((KumaMonitorStatus)(int)sample.Value) => monitor with
                                                                                                      {
                                                                                                        Status =
                                                                                                        (KumaMonitorStatus)
                                                                                                        (int)sample.Value,
                                                                                                      },
                       "monitor_response_time" when double.IsFinite (sample.Value) && (sample.Value >= 0) => monitor with
                                                                                                             {
                                                                                                               ResponseTimeMs =
                                                                                                               sample.Value,
                                                                                                             },
                       "monitor_uptime_ratio" when sample.Labels.TryGetValue (key: "window", value: out string? window) &&
                                                   double.IsFinite (sample.Value) => KumaMetrics.WithUptime (monitor: monitor,
                                                                                                             window: window,
                                                                                                             ratio: sample.Value),
                       "monitor_cert_days_remaining" when double.IsFinite (sample.Value) => monitor with
                                                                                            {
                                                                                              CertDaysRemaining =
                                                                                              (int)sample.Value,
                                                                                            },
                       "monitor_cert_is_valid" => monitor with { CertIsValid = sample.Value >= 1 },
                       _                       => monitor,
                     };
    }

    return new KumaMetrics (Monitors: monitors, Message: null);
  }

  /// <summary>
  ///   Adds an uptime ratio to a monitor's metrics.
  /// </summary>
  /// <param name="monitor">The monitor.</param>
  /// <param name="window">The window, such as <c>1d</c>.</param>
  /// <param name="ratio">The ratio, from 0 to 1.</param>
  /// <returns>The monitor with the ratio.</returns>
  private static KumaMonitorMetrics WithUptime (KumaMonitorMetrics monitor, string window, double ratio)
    => monitor with
       {
         Uptime = new Dictionary<string, double> (collection: monitor.Uptime, comparer: StringComparer.Ordinal)
                  {
                    [window] = Math.Clamp (value: ratio, min: 0, max: 1),
                  },
       };

  /// <summary>
  ///   Lists the monitors for a monitor picker: group monitors first, then the rest, each by name.
  /// </summary>
  /// <returns>The monitor list.</returns>
  public KumaMonitorList ToMonitorList ()
  {
    if (this.Message != null)
    {
      return KumaMonitorList.Failed (this.Message);
    }

    var groups = new List<KumaMonitorGroup> ();

    foreach (bool isGroup in new[] { true, false })
    {
      List<KumaMonitor> monitors =
      [
        .. this.Monitors.Values
               .Where (m => m.IsGroup == isGroup)
               .OrderBy (keySelector: m => m.Name, comparer: StringComparer.CurrentCultureIgnoreCase)
               .ThenBy (m => m.Id)
               .Select (m => new KumaMonitor (Id: m.Id, Name: m.Name)),
      ];

      if (monitors.Count > 0)
      {
        groups.Add (new KumaMonitorGroup (Name: isGroup ? "Groups" : "Monitors", Monitors: monitors));
      }
    }

    return new KumaMonitorList (Groups: groups, Message: null);
  }
}

namespace h2s.Services;

/// <summary>
///   What Uptime Kuma's <c>/metrics</c> endpoint reports about one monitor.
/// </summary>
/// <param name="Id">The monitor ID.</param>
/// <param name="Name">The monitor's name.</param>
/// <param name="Type">The monitor's type, such as <c>http</c>, <c>ping</c> or <c>group</c>.</param>
/// <param name="Status">The monitor's status, if Kuma reported one.</param>
/// <param name="ResponseTimeMs">The latest response time in milliseconds, if Kuma reported one.</param>
/// <param name="Uptime">The uptime ratio (0 to 1) for each window Kuma reported, keyed by window (<c>1d</c>, <c>30d</c>, <c>365d</c>).</param>
/// <param name="CertDaysRemaining">The days left on the monitored certificate, for HTTPS monitors.</param>
/// <param name="CertIsValid">Whether the monitored certificate is valid, for HTTPS monitors.</param>
public sealed record KumaMonitorMetrics (
  int                                 Id,
  string                              Name,
  string                              Type,
  KumaMonitorStatus?                  Status,
  double?                             ResponseTimeMs,
  IReadOnlyDictionary<string, double> Uptime,
  int?                                CertDaysRemaining,
  bool?                               CertIsValid)
{
  /// <summary>
  ///   Gets a value indicating whether the monitor is a group of other monitors.
  /// </summary>
  public bool IsGroup => this.Type == "group";
}

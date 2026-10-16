namespace h2s.Services;

/// <summary>
///   A Uptime Kuma monitor's status, as <c>monitor_status</c> reports it.
/// </summary>
public enum KumaMonitorStatus
{
  /// <summary>The monitor is down.</summary>
  Down = 0,

  /// <summary>The monitor is up.</summary>
  Up = 1,

  /// <summary>The monitor is waiting to confirm a change.</summary>
  Pending = 2,

  /// <summary>The monitor is in a maintenance window.</summary>
  Maintenance = 3,
}

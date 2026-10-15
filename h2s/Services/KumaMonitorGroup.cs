namespace h2s.Services;

/// <summary>
///   A group of monitors on an Uptime Kuma status page.
/// </summary>
/// <param name="Name">The group name.</param>
/// <param name="Monitors">The group's monitors, in order.</param>
public sealed record KumaMonitorGroup (string Name, IReadOnlyList<KumaMonitor> Monitors);

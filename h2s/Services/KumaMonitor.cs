namespace h2s.Services;

/// <summary>
///   A monitor on an Uptime Kuma status page.
/// </summary>
/// <param name="Id">The monitor ID, as stored on links and infrastructure groups.</param>
/// <param name="Name">The monitor name.</param>
public sealed record KumaMonitor (int Id, string Name);

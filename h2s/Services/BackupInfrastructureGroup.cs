namespace h2s.Services;

/// <summary>
///   An infrastructure group in a <see cref="DashboardBackup" />.
/// </summary>
/// <param name="Name">The group name.</param>
/// <param name="MonitorId">The Uptime Kuma monitor ID.</param>
public sealed record BackupInfrastructureGroup (string? Name, int MonitorId);

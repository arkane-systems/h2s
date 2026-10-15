namespace h2s.Services;

/// <summary>
///   A link in a <see cref="DashboardBackup" />.
/// </summary>
/// <param name="Label">The link label.</param>
/// <param name="Url">The link URL.</param>
/// <param name="Description">The link description.</param>
/// <param name="IconName">The selfh.st icon name.</param>
/// <param name="IconUrl">The link's own icon image URL.</param>
/// <param name="MonitorId">The Uptime Kuma monitor ID, if any.</param>
public sealed record BackupLink (
  string? Label,
  string? Url,
  string? Description,
  string? IconName,
  string? IconUrl,
  int?    MonitorId);

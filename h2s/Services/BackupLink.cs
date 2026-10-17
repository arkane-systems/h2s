using System.Text.Json.Serialization;

namespace h2s.Services;

/// <summary>
///   A link in a <see cref="DashboardBackup" />.
/// </summary>
/// <param name="Label">The link label.</param>
/// <param name="Url">The link URL.</param>
/// <param name="Description">The link description.</param>
/// <param name="IconName">The selfh.st icon name.</param>
/// <param name="IconUrl">The link's own icon image URL.</param>
/// <param name="OpenInNewTab">Whether the link opens in a new browser tab; from format version 4, and off before it.</param>
/// <param name="Widgets">The link's widgets, in order; from format version 7, and none before it.</param>
/// <param name="MonitorId">
///   The link's Uptime Kuma monitor ID, if any; only in files before format version 8. Reading such a file turns it into an
///   Uptime Kuma monitor widget, unless the link already has one for that monitor.
/// </param>
public sealed record BackupLink (
  string?             Label,
  string?             Url,
  string?             Description,
  string?             IconName,
  string?             IconUrl,
  bool                OpenInNewTab = false,
  List<BackupWidget>? Widgets      = null,
  [property: JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
  int? MonitorId = null);

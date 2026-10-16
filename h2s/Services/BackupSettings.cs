using System.Text.Json.Serialization;
using h2s.Models;

namespace h2s.Services;

/// <summary>
///   The dashboard settings in a <see cref="DashboardBackup" />.
/// </summary>
/// <param name="Title">The dashboard title.</param>
/// <param name="Motto">The motto under the title.</param>
/// <param name="LocalDomains">The local domains, as entered in Settings.</param>
/// <param name="ColorMode">The color mode.</param>
/// <param name="UptimeKumaServerUrl">
///   The Uptime Kuma server URL, if any; only in files before format version 5, which keep Uptime Kuma's settings in a
///   connection instead. Reading such a file turns these three values into that connection.
/// </param>
/// <param name="UptimeKumaStatusPageSlug">The Uptime Kuma status page slug, if any; only before format version 5.</param>
/// <param name="UptimeKumaDefaultDuration">The default uptime badge duration, in hours; only before format version 5.</param>
public sealed record BackupSettings (
  string?   Title,
  string?   Motto,
  string?   LocalDomains,
  ColorMode ColorMode,
  [property: JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
  string? UptimeKumaServerUrl = null,
  [property: JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
  string? UptimeKumaStatusPageSlug = null,
  [property: JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
  int? UptimeKumaDefaultDuration = null);

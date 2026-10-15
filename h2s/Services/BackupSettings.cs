using h2s.Models;

namespace h2s.Services;

/// <summary>
///   The dashboard settings in a <see cref="DashboardBackup" />.
/// </summary>
/// <param name="Title">The dashboard title.</param>
/// <param name="Motto">The motto under the title.</param>
/// <param name="LocalDomains">The local domains, as entered in Settings.</param>
/// <param name="ColorMode">The color mode.</param>
/// <param name="UptimeKumaServerUrl">The Uptime Kuma server URL, if any.</param>
/// <param name="UptimeKumaStatusPageSlug">The Uptime Kuma status page slug, if any.</param>
/// <param name="UptimeKumaDefaultDuration">The default uptime badge duration, in hours.</param>
public sealed record BackupSettings (
  string?   Title,
  string?   Motto,
  string?   LocalDomains,
  ColorMode ColorMode,
  string?   UptimeKumaServerUrl,
  string?   UptimeKumaStatusPageSlug,
  int       UptimeKumaDefaultDuration);

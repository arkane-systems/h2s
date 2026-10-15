namespace h2s.Panels;

/// <summary>
///   One item on the status bar: an infrastructure group's badges.
/// </summary>
/// <param name="Name">The group's name, shown above its badges.</param>
/// <param name="StatusBadgeUrl">The Uptime Kuma status badge URL.</param>
/// <param name="UptimeBadgeUrl">The Uptime Kuma uptime badge URL, or <c>null</c> when uptime isn't shown.</param>
public sealed record StatusBarItem (string Name, string StatusBadgeUrl, string? UptimeBadgeUrl);

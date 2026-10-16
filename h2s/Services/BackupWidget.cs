using System.Text.Json;

namespace h2s.Services;

/// <summary>
///   A widget on a link in a <see cref="DashboardBackup" />, from format version 7.
/// </summary>
/// <param name="Kind">The widget's kind, as <c>integration/kind</c>, such as <c>uptime-kuma/monitor</c>.</param>
/// <param name="Connection">The name of the connection it uses, one of the file's connections of that integration.</param>
/// <param name="Options">The kind's option values, as a JSON object keyed by option name; <c>null</c> for all defaults.</param>
/// <param name="RefreshSeconds">Its refresh interval in seconds, or <c>null</c> for its kind's default.</param>
public sealed record BackupWidget (string? Kind, string? Connection, JsonElement? Options, int? RefreshSeconds = null);

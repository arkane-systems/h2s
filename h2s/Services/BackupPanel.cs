using System.Text.Json;
using h2s.Models;

namespace h2s.Services;

/// <summary>
///   A panel in a <see cref="DashboardBackup" />.
/// </summary>
/// <param name="Kind">The panel's kind key.</param>
/// <param name="Zone">Whether the panel sits above or below the categories.</param>
/// <param name="Options">The kind's option values, as a JSON object keyed by option name; <c>null</c> for all defaults.</param>
public sealed record BackupPanel (string? Kind, PanelZone Zone, JsonElement? Options);

using System.Text.Json;

namespace h2s.Services;

/// <summary>
///   A connection in a <see cref="DashboardBackup" />. Its secrets are never exported.
/// </summary>
/// <param name="Integration">The key of the connection's integration.</param>
/// <param name="Name">The connection's name.</param>
/// <param name="Options">
///   The integration's settings, except secrets, as a JSON object keyed by option name; <c>null</c> for all defaults.
/// </param>
public sealed record BackupConnection (string? Integration, string? Name, JsonElement? Options);

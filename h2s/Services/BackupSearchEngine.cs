namespace h2s.Services;

/// <summary>
///   A search engine in a <see cref="DashboardBackup" />.
/// </summary>
/// <param name="Name">The engine's name.</param>
/// <param name="UrlTemplate">The engine's search URL, with <c>{query}</c> where the terms go.</param>
/// <param name="Shortcut">The engine's shortcut, without its <c>!</c>; blank or missing for none.</param>
/// <param name="IsDefault">Whether the engine is the default.</param>
public sealed record BackupSearchEngine (string? Name, string? UrlTemplate, string? Shortcut, bool IsDefault);

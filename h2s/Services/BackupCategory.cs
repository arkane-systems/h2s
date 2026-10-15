namespace h2s.Services;

/// <summary>
///   A category and its links in a <see cref="DashboardBackup" />.
/// </summary>
/// <param name="Name">The category name.</param>
/// <param name="IsAdminCategory">Whether the category is in the admin section.</param>
/// <param name="Links">The category's links, in order.</param>
public sealed record BackupCategory (string? Name, bool IsAdminCategory, List<BackupLink>? Links);

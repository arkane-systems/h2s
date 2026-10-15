namespace h2s.Services;

/// <summary>
///   What a <see cref="DashboardBackup" /> holds, shown before an import replaces the current configuration with it.
/// </summary>
/// <param name="Title">The dashboard title in the file.</param>
/// <param name="ExportedAt">When the file was exported, if it says.</param>
/// <param name="Categories">The number of categories.</param>
/// <param name="AdminCategories">How many of those are admin categories.</param>
/// <param name="Links">The number of links.</param>
/// <param name="InfrastructureGroups">The number of infrastructure groups.</param>
/// <param name="Panels">The number of panels; for a file from before panels, the number of default panels.</param>
public sealed record BackupSummary (
  string          Title,
  DateTimeOffset? ExportedAt,
  int             Categories,
  int             AdminCategories,
  int             Links,
  int             InfrastructureGroups,
  int             Panels);

using System.Text.Json;
using h2s.Data;
using h2s.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace h2s.Services;

/// <summary>
///   Exports the whole dashboard configuration to a <see cref="DashboardBackup" />, and replaces it with one on import.
/// </summary>
public class DashboardBackupService
{
  // Enough to show what's wrong with a file without burying it.
  private const int MaxErrors = 10;

  private readonly DashboardContext         context;
  private readonly DashboardSettingsService settingsService;

  /// <summary>
  ///   Initializes a new instance of the <see cref="DashboardBackupService" /> class.
  /// </summary>
  /// <param name="context">The database context holding the configuration.</param>
  /// <param name="settingsService">The service through which settings are read and saved.</param>
  public DashboardBackupService (DashboardContext context, DashboardSettingsService settingsService)
  {
    this.context         = context;
    this.settingsService = settingsService;
  }

  /// <summary>
  ///   Exports the current configuration.
  /// </summary>
  /// <param name="cancellationToken">A token used to cancel the queries.</param>
  /// <returns>The configuration, in dashboard order.</returns>
  public async Task<DashboardBackup> ExportAsync (CancellationToken cancellationToken = default)
  {
    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();

    List<Category> categories = await this.context.Categories
                                          .AsNoTracking ()
                                          .Include (c => c.Links)
                                          .OrderBy (c => c.IsAdminCategory)
                                          .ThenBy (c => c.SortOrder)
                                          .ThenBy (c => c.Name)
                                          .ToListAsync (cancellationToken);

    List<InfrastructureGroup> groups = await this.context.InfrastructureGroups
                                                 .AsNoTracking ()
                                                 .OrderBy (g => g.SortOrder)
                                                 .ThenBy (g => g.Name)
                                                 .ToListAsync (cancellationToken);

    return new DashboardBackup (Format: DashboardBackup.FormatName,
                                Version: DashboardBackup.CurrentVersion,
                                ExportedAt: DateTimeOffset.UtcNow,
                                Settings: new BackupSettings (Title: settings.Title,
                                                              Motto: settings.Motto,
                                                              LocalDomains: settings.LocalDomains,
                                                              ColorMode: settings.ColorMode,
                                                              UptimeKumaServerUrl: settings.UptimeKumaServerUrl,
                                                              UptimeKumaStatusPageSlug: settings.UptimeKumaStatusPageSlug,
                                                              UptimeKumaDefaultDuration: settings.UptimeKumaDefaultDuration),
                                Categories:
                                [
                                  .. categories.Select (c => new BackupCategory (Name: c.Name,
                                                                                 IsAdminCategory: c.IsAdminCategory,
                                                                                 Links:
                                                                                 [
                                                                                   .. c.Links.OrderBy (l => l.SortOrder)
                                                                                       .ThenBy (l => l.Label)
                                                                                       .Select (l => new BackupLink (Label: l.Label,
                                                                                                                     Url: l.Url,
                                                                                                                     Description: l
                                                                                                                      .Description,
                                                                                                                     IconName: l
                                                                                                                      .IconName,
                                                                                                                     IconUrl: l
                                                                                                                      .IconUrl,
                                                                                                                     MonitorId: l
                                                                                                                      .MonitorId)),
                                                                                 ])),
                                ],
                                InfrastructureGroups:
                                [
                                  .. groups.Select (g => new BackupInfrastructureGroup (Name: g.Name, MonitorId: g.MonitorId)),
                                ]);
  }

  /// <summary>
  ///   Reads and checks a dashboard file.
  /// </summary>
  /// <param name="json">The file's contents.</param>
  /// <param name="backup">The configuration, when the file is valid.</param>
  /// <param name="errors">What's wrong with the file, when it isn't; at most ten problems.</param>
  /// <returns><c>true</c> when the file is a valid dashboard configuration that can be imported.</returns>
  public static bool TryRead (string json, out DashboardBackup? backup, out List<string> errors)
  {
    errors = [];
    backup = null;

    DashboardBackup? read;

    try
    {
      read = JsonSerializer.Deserialize<DashboardBackup> (json: json, options: DashboardBackup.JsonOptions);
    }
    catch (JsonException ex)
    {
      errors.Add ($"The file isn't valid JSON for a dashboard: {ex.Message}");

      return false;
    }

    if (read is not { Format: DashboardBackup.FormatName })
    {
      errors.Add ("The file isn't an h2s dashboard export.");

      return false;
    }

    if (read.Version != DashboardBackup.CurrentVersion)
    {
      errors.Add (read.Version > DashboardBackup.CurrentVersion
                    ? $"The file is from a newer version of h2s (format version {read.Version}); update h2s to import it."
                    : $"The file's format version ({read.Version}) isn't supported.");

      return false;
    }

    DashboardBackupService.Validate (backup: read, errors: errors);

    if (errors.Count > 0)
      return false;

    backup = read;

    return true;
  }

  /// <summary>
  ///   Summarizes what a valid dashboard file holds.
  /// </summary>
  /// <param name="backup">The configuration.</param>
  /// <returns>The summary.</returns>
  public static BackupSummary Summarize (DashboardBackup backup)
    => new (Title: backup.Settings?.Title ?? "",
            ExportedAt: backup.ExportedAt,
            Categories: backup.Categories?.Count                               ?? 0,
            AdminCategories: backup.Categories?.Count (c => c.IsAdminCategory) ?? 0,
            Links: backup.Categories?.Sum (c => c.Links?.Count ?? 0)           ?? 0,
            InfrastructureGroups: backup.InfrastructureGroups?.Count           ?? 0);

  /// <summary>
  ///   Replaces the whole configuration with a valid dashboard file's, in one transaction.
  /// </summary>
  /// <param name="backup">The configuration, already checked by <see cref="TryRead" />.</param>
  /// <param name="cancellationToken">A token used to cancel the import, which then changes nothing.</param>
  public async Task ImportAsync (DashboardBackup backup, CancellationToken cancellationToken = default)
  {
    await using IDbContextTransaction transaction = await this.context.Database.BeginTransactionAsync (cancellationToken);

    _ = await this.context.Links.ExecuteDeleteAsync (cancellationToken);
    _ = await this.context.Categories.ExecuteDeleteAsync (cancellationToken);
    _ = await this.context.InfrastructureGroups.ExecuteDeleteAsync (cancellationToken);

    // Positions count within each section, as on the dashboard.
    int regular = 0, admin = 0;

    foreach (BackupCategory category in backup.Categories ?? [])
    {
      this.context.Categories.Add (new Category
                                   {
                                     Name            = category.Name!.Trim (),
                                     IsAdminCategory = category.IsAdminCategory,
                                     SortOrder       = category.IsAdminCategory ? admin++ : regular++,
                                     Links =
                                     [
                                       .. (category.Links ?? []).Select ((l, i) => new Link
                                                                                   {
                                                                                     Label       = l.Label!.Trim (),
                                                                                     Url         = l.Url!.Trim (),
                                                                                     Description = (l.Description ?? "").Trim (),
                                                                                     IconName =
                                                                                       Link.NormalizeIconName (l.IconName ?? ""),
                                                                                     IconUrl   = (l.IconUrl ?? "").Trim (),
                                                                                     MonitorId = l.MonitorId,
                                                                                     SortOrder = i,
                                                                                   }),
                                     ],
                                   });
    }

    this.context.InfrastructureGroups.AddRange ((backup.InfrastructureGroups ?? []).Select ((g, i) => new InfrastructureGroup
                                                                                                      {
                                                                                                        Name      = g.Name!.Trim (),
                                                                                                        MonitorId = g.MonitorId,
                                                                                                        SortOrder = i,
                                                                                                      }));

    await this.context.SaveChangesAsync (cancellationToken);

    BackupSettings    imported = backup.Settings!;
    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();

    settings.Title        = imported.Title!.Trim ();
    settings.Motto        = (imported.Motto        ?? "").Trim ();
    settings.LocalDomains = (imported.LocalDomains ?? "").Trim ();
    settings.ColorMode    = imported.ColorMode;
    settings.UptimeKumaServerUrl =
      string.IsNullOrWhiteSpace (imported.UptimeKumaServerUrl) ? null : imported.UptimeKumaServerUrl.Trim ();
    settings.UptimeKumaStatusPageSlug = string.IsNullOrWhiteSpace (imported.UptimeKumaStatusPageSlug)
                                          ? null
                                          : imported.UptimeKumaStatusPageSlug.Trim ();
    settings.UptimeKumaDefaultDuration = imported.UptimeKumaDefaultDuration;

    await this.settingsService.SaveSettingsAsync (settings);
    await transaction.CommitAsync (cancellationToken);
  }

  /// <summary>
  ///   Checks a dashboard file's contents, collecting up to ten problems.
  /// </summary>
  /// <param name="backup">The configuration.</param>
  /// <param name="errors">The list the problems are added to.</param>
  private static void Validate (DashboardBackup backup, List<string> errors)
  {
    void Add (string error)
    {
      if (errors.Count < DashboardBackupService.MaxErrors)
        errors.Add (error);
    }

    if (backup.Settings is not { } settings)
      Add ("The file has no settings.");
    else
    {
      if (string.IsNullOrWhiteSpace (settings.Title))
        Add ("Settings: the title is required.");

      if (!Enum.IsDefined (settings.ColorMode))
        Add ("Settings: the color mode isn't one of Auto, Light or Dark.");

      if (!string.IsNullOrWhiteSpace (settings.UptimeKumaServerUrl) &&
          !DashboardBackupService.IsWebUrl (settings.UptimeKumaServerUrl))
        Add ("Settings: the Uptime Kuma server URL must be an HTTP or HTTPS URL.");

      if (settings.UptimeKumaDefaultDuration <= 0)
        Add ("Settings: the default uptime duration must be a positive number of hours.");
    }

    if (backup.Categories == null)
      Add ("The file has no categories list.");

    for (var c = 0; c < (backup.Categories?.Count ?? 0); c++)
    {
      BackupCategory category = backup.Categories![c];
      var            where    = $"Category {c + 1}";

      if (string.IsNullOrWhiteSpace (category.Name))
        Add ($"{where}: the name is required.");
      else
        where += $" (\"{category.Name.Trim ()}\")";

      for (var l = 0; l < (category.Links?.Count ?? 0); l++)
      {
        BackupLink link     = category.Links![l];
        var        linkName = $"{where}, link {l + 1}";

        if (string.IsNullOrWhiteSpace (link.Label))
          Add ($"{linkName}: the label is required.");

        if (string.IsNullOrWhiteSpace (link.Url) || !DashboardBackupService.IsWebUrl (link.Url))
          Add ($"{linkName}: the URL must be an HTTP or HTTPS URL.");

        if (!string.IsNullOrWhiteSpace (link.IconUrl) && !DashboardBackupService.IsWebUrl (link.IconUrl))
          Add ($"{linkName}: the icon URL must be an HTTP or HTTPS URL.");

        if (link.MonitorId is <= 0)
          Add ($"{linkName}: the monitor ID must be a positive integer.");
      }
    }

    for (var g = 0; g < (backup.InfrastructureGroups?.Count ?? 0); g++)
    {
      BackupInfrastructureGroup group = backup.InfrastructureGroups![g];

      if (string.IsNullOrWhiteSpace (group.Name))
        Add ($"Infrastructure group {g + 1}: the name is required.");

      if (group.MonitorId <= 0)
        Add ($"Infrastructure group {g + 1}: the monitor ID must be a positive integer.");
    }
  }

  /// <summary>
  ///   Determines whether a string is an absolute HTTP or HTTPS URL.
  /// </summary>
  /// <param name="value">The string.</param>
  /// <returns><c>true</c> when it is.</returns>
  private static bool IsWebUrl (string value)
    => Uri.TryCreate (uriString: value.Trim (), uriKind: UriKind.Absolute, result: out Uri? uri) &&
       ((uri.Scheme == Uri.UriSchemeHttp) || (uri.Scheme == Uri.UriSchemeHttps));
}

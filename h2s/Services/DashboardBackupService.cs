using System.Text.Json;
using h2s.Data;
using h2s.Models;
using h2s.Panels;
using h2s.Validation;
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

    List<Panel> panels = await this.context.Panels
                                   .AsNoTracking ()
                                   .OrderBy (p => p.Zone)
                                   .ThenBy (p => p.SortOrder)
                                   .ThenBy (p => p.Id)
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
                                ],
                                Panels:
                                [
                                  .. panels.Select (p => new BackupPanel (Kind: p.Kind,
                                                                          Zone: p.Zone,
                                                                          Options: DashboardBackupService
                                                                           .ReadOptions (p.OptionsJson))),
                                ]);
  }

  /// <summary>
  ///   Reads and checks a dashboard file.
  /// </summary>
  /// <param name="json">The file's contents.</param>
  /// <param name="kinds">The panel kinds the file's panels may use.</param>
  /// <param name="backup">
  ///   The configuration, when the file is valid. A file from before panels comes back with no panels list (and any it
  ///   holds anyway ignored).
  /// </param>
  /// <param name="errors">What's wrong with the file, when it isn't; at most ten problems.</param>
  /// <returns><c>true</c> when the file is a valid dashboard configuration that can be imported.</returns>
  public static bool TryRead (string json, PanelCatalog kinds, out DashboardBackup? backup, out List<string> errors)
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

    if (read.Version is < 1 or > DashboardBackup.CurrentVersion)
    {
      errors.Add (read.Version > DashboardBackup.CurrentVersion
                    ? $"The file is from a newer version of h2s (format version {read.Version}); update h2s to import it."
                    : $"The file's format version ({read.Version}) isn't supported.");

      return false;
    }

    if (read.Version < DashboardBackup.PanelsVersion)
      read = read with { Panels = null };

    DashboardBackupService.Validate (backup: read, kinds: kinds, errors: errors);

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
            InfrastructureGroups: backup.InfrastructureGroups?.Count           ?? 0,
            Panels: backup.Panels?.Count                                       ?? DefaultPanels.Create ().Count);

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
    _ = await this.context.Panels.ExecuteDeleteAsync (cancellationToken);

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

    // A file from before panels gets the default ones. Positions count within each zone.
    if (backup.Panels == null)
      this.context.Panels.AddRange (DefaultPanels.Create ());
    else
    {
      int top = 0, bottom = 0;

      this.context.Panels.AddRange (backup.Panels.Select (p => new Panel
                                                               {
                                                                 Kind      = p.Kind!.Trim (),
                                                                 Zone      = p.Zone,
                                                                 SortOrder = p.Zone == PanelZone.Top ? top++ : bottom++,

                                                                 // Stored compact, however the file was indented.
                                                                 OptionsJson = p.Options is
                                                                               {
                                                                                 ValueKind: JsonValueKind.Object,
                                                                               } options
                                                                                 ? JsonSerializer.Serialize (options)
                                                                                 : "{}",
                                                               }));
    }

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
  /// <param name="kinds">The panel kinds the file's panels may use.</param>
  /// <param name="errors">The list the problems are added to.</param>
  private static void Validate (DashboardBackup backup, PanelCatalog kinds, List<string> errors)
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

      if (!StatusPageSlug.IsValid (settings.UptimeKumaStatusPageSlug))
        Add ("Settings: the status page slug may only contain letters, numbers, and hyphens.");

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

    if ((backup.Version >= DashboardBackup.PanelsVersion) && (backup.Panels == null))
      Add ("The file has no panels list.");

    var seenKinds = new HashSet<string> (StringComparer.Ordinal);

    for (var p = 0; p < (backup.Panels?.Count ?? 0); p++)
    {
      BackupPanel panel = backup.Panels![p];
      var         where = $"Panel {p + 1}";

      if (string.IsNullOrWhiteSpace (panel.Kind))
        Add ($"{where}: the kind is required.");
      else if (kinds.Find (panel.Kind.Trim ()) is not { } kind)
        Add ($"{where}: h2s has no “{panel.Kind.Trim ()}” panel kind.");
      else if (!seenKinds.Add (kind.Key) && kind.IsSingleInstance)
        Add ($"{where}: the dashboard can only have one {kind.DisplayName.ToLowerInvariant ()} panel.");

      if (!Enum.IsDefined (panel.Zone))
        Add ($"{where}: the zone isn't one of Top or Bottom.");

      if (panel.Options is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) })
        Add ($"{where}: the options must be a JSON object.");
    }
  }

  /// <summary>
  ///   Reads a panel's stored options for export.
  /// </summary>
  /// <param name="json">The stored options.</param>
  /// <returns>The options as a JSON object; an empty one when what's stored isn't one.</returns>
  private static JsonElement ReadOptions (string json)
  {
    try
    {
      using JsonDocument document = JsonDocument.Parse (json);

      if (document.RootElement.ValueKind == JsonValueKind.Object)
        return document.RootElement.Clone ();
    }
    catch (JsonException)
    {
      // Exported as no options, which read as the defaults.
    }

    using JsonDocument empty = JsonDocument.Parse ("{}");

    return empty.RootElement.Clone ();
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

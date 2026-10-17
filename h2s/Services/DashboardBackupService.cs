using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using h2s.Data;
using h2s.Integrations;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Options;
using h2s.Panels;
using h2s.Validation;
using h2s.Widgets;
using h2s.Widgets.Abstractions;
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

  private readonly ConnectionService        connections;
  private readonly DashboardContext         context;
  private readonly IntegrationCatalog       integrations;
  private readonly DashboardSettingsService settingsService;

  /// <summary>
  ///   Initializes a new instance of the <see cref="DashboardBackupService" /> class.
  /// </summary>
  /// <param name="context">The database context holding the configuration.</param>
  /// <param name="settingsService">The service through which settings are read and saved.</param>
  /// <param name="connections">The service whose cached connections an import replaces.</param>
  /// <param name="integrations">The integrations whose schemas imported connections are stored against.</param>
  public DashboardBackupService (DashboardContext         context,
                                 DashboardSettingsService settingsService,
                                 ConnectionService        connections,
                                 IntegrationCatalog       integrations)
  {
    this.context         = context;
    this.settingsService = settingsService;
    this.connections     = connections;
    this.integrations    = integrations;
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
                                          .ThenInclude (l => l.Widgets)
                                          .OrderBy (c => c.IsAdminCategory)
                                          .ThenBy (c => c.SortOrder)
                                          .ThenBy (c => c.Name)
                                          .ToListAsync (cancellationToken);

    List<SearchEngine> engines = await this.context.SearchEngines
                                           .AsNoTracking ()
                                           .OrderBy (e => e.SortOrder)
                                           .ThenBy (e => e.Id)
                                           .ToListAsync (cancellationToken);

    List<Panel> panels = await this.context.Panels
                                   .AsNoTracking ()
                                   .Include (p => p.Widgets)
                                   .OrderBy (p => p.Zone)
                                   .ThenBy (p => p.SortOrder)
                                   .ThenBy (p => p.Id)
                                   .ToListAsync (cancellationToken);

    List<Connection> connections = await this.context.Connections
                                             .AsNoTracking ()
                                             .OrderBy (c => c.IntegrationKey)
                                             .ThenBy (c => c.Name)
                                             .ThenBy (c => c.Id)
                                             .ToListAsync (cancellationToken);

    Dictionary<int, Connection> connectionsById = connections.ToDictionary (c => c.Id);

    return new DashboardBackup (Format: DashboardBackup.FormatName,
                                Version: DashboardBackup.CurrentVersion,
                                ExportedAt: DateTimeOffset.UtcNow,
                                Settings: new BackupSettings (Title: settings.Title,
                                                              Motto: settings.Motto,
                                                              LocalDomains: settings.LocalDomains,
                                                              ColorMode: settings.ColorMode),
                                Categories:
                                [
                                  .. categories.Select (c => new BackupCategory (Name: c.Name,
                                                                                 IsAdminCategory: c.IsAdminCategory,
                                                                                 Links:
                                                                                 [
                                                                                   .. c.Links.OrderBy (l => l.SortOrder)
                                                                                       .ThenBy (l => l.Label)
                                                                                       .Select (l => DashboardBackupService
                                                                                                 .ExportLink (link: l,
                                                                                                              connections:
                                                                                                              connectionsById)),
                                                                                 ])),
                                ],
                                InfrastructureGroups: null,
                                Panels:
                                [
                                  .. panels.Select (p => DashboardBackupService.ExportPanel (panel: p,
                                                                                             connections: connectionsById)),
                                ],
                                SearchEngines:
                                [
                                  .. engines.Select (e => new BackupSearchEngine (Name: e.Name,
                                                                                  UrlTemplate: e.UrlTemplate,
                                                                                  Shortcut: e.Shortcut,
                                                                                  IsDefault: e.IsDefault)),
                                ],
                                Connections:
                                [
                                  .. connections.Select (c => new BackupConnection (Integration: c.IntegrationKey,
                                                                                    Name: c.Name,
                                                                                    Options: DashboardBackupService
                                                                                     .ReadOptions (c.OptionsJson))),
                                ]);
  }

  /// <summary>
  ///   Reads and checks a dashboard file.
  /// </summary>
  /// <param name="json">The file's contents.</param>
  /// <param name="kinds">The panel kinds the file's panels may use.</param>
  /// <param name="integrations">The integrations the file's connections may use.</param>
  /// <param name="backup">
  ///   The configuration, when the file is valid. A file from before panels comes back with no panels list (and any it
  ///   holds anyway ignored), and a file from before connections with its Uptime Kuma settings as a connection.
  /// </param>
  /// <param name="errors">What's wrong with the file, when it isn't; at most ten problems.</param>
  /// <returns><c>true</c> when the file is a valid dashboard configuration that can be imported.</returns>
  public static bool TryRead (string               json,
                              PanelCatalog         kinds,
                              IntegrationCatalog   integrations,
                              out DashboardBackup? backup,
                              out List<string>     errors)
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
    {
      read = read with { Panels = null };
    }

    if (read.Version < DashboardBackup.SearchEnginesVersion)
    {
      read = read with { SearchEngines = null };
    }

    if (read.Version < DashboardBackup.ConnectionsVersion)
    {
      read = read with { Connections = DashboardBackupService.ConnectionsFromSettings (read.Settings) };
    }

    if (read.Version < DashboardBackup.UptimeWindowsVersion)
    {
      read = DashboardBackupService.UseUptimeWindows (read);
    }

    if (read.Version < DashboardBackup.WidgetsVersion)
    {
      read = read with
             {
               Categories = read.Categories
                               ?.Select (c => c with { Links = c.Links?.Select (l => l with { Widgets = null }).ToList () })
                                .ToList (),
             };
    }

    if (read.Version < DashboardBackup.MonitorWidgetsVersion)
    {
      read = DashboardBackupService.UseMonitorWidgets (read);
    }

    if (read.Version < DashboardBackup.StatusBarWidgetsVersion)
    {
      read = DashboardBackupService.UseStatusBarWidgets (read);
    }
    else
    {
      read = read with { InfrastructureGroups = null };
    }

    DashboardBackupService.Validate (backup: read, kinds: kinds, integrations: integrations, errors: errors);

    if (errors.Count > 0)
    {
      return false;
    }

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
            Panels: backup.Panels?.Count                                       ?? DefaultPanels.Create ().Count,
            SearchEngines: backup.SearchEngines?.Count                         ?? DefaultSearchEngines.Create ().Count,
            Connections: backup.Connections?.Count                             ?? 0,
            Widgets: (backup.Categories?.Sum (c => c.Links?.Sum (l => l.Widgets?.Count ?? 0) ?? 0) ?? 0) +
                     (backup.Panels?.Sum (p => p.Widgets?.Count                              ?? 0) ?? 0));

  /// <summary>
  ///   Replaces the whole configuration with a valid dashboard file's, in one transaction.
  /// </summary>
  /// <param name="backup">The configuration, already checked by <see cref="TryRead" />.</param>
  /// <param name="cancellationToken">A token used to cancel the import, which then changes nothing.</param>
  public async Task ImportAsync (DashboardBackup backup, CancellationToken cancellationToken = default)
  {
    await using IDbContextTransaction transaction = await this.context.Database.BeginTransactionAsync (cancellationToken);

    // Secrets aren't in the file, so a connection keeps those of the one it replaces: same integration, same name.
    Dictionary<(string IntegrationKey, string Name), string?> secrets =
      await this.context.Connections
                .AsNoTracking ()
                .Where (c => c.SecretsJson != null)
                .ToDictionaryAsync (keySelector: c => (c.IntegrationKey, c.Name),
                                    elementSelector: c => c.SecretsJson,
                                    cancellationToken: cancellationToken);

    _ = await this.context.Links.ExecuteDeleteAsync (cancellationToken);
    _ = await this.context.Categories.ExecuteDeleteAsync (cancellationToken);
    _ = await this.context.Panels.ExecuteDeleteAsync (cancellationToken);
    _ = await this.context.SearchEngines.ExecuteDeleteAsync (cancellationToken);
    _ = await this.context.Connections.ExecuteDeleteAsync (cancellationToken);

    // Connections first, so the links' and panels' widgets can use them.
    var importedConnections = new Dictionary<(string IntegrationKey, string Name), Connection> ();
    var widgetCatalog       = new WidgetCatalog (this.integrations);

    foreach (BackupConnection connection in backup.Connections ?? [])
    {
      IIntegration integration = this.integrations.Find (connection.Integration!.Trim ())!;
      string       name        = connection.Name!.Trim ();

      _ = DashboardBackupService.TryReadConnectionOptions (integration: integration,
                                                           options: connection.Options,
                                                           json: out string? json,
                                                           error: out _);

      var entity = new Connection
                   {
                     IntegrationKey = integration.Key,
                     Name           = name,
                     OptionsJson    = json!,
                     SecretsJson    = secrets.GetValueOrDefault ((integration.Key, name)),
                   };

      importedConnections[(integration.Key, name)] = entity;
      this.context.Connections.Add (entity);
    }

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
                                                                                     IconUrl      = (l.IconUrl ?? "").Trim (),
                                                                                     OpenInNewTab = l.OpenInNewTab,
                                                                                     SortOrder    = i,
                                                                                     Widgets = DashboardBackupService
                                                                                      .ImportWidgets (widgets: l.Widgets,
                                                                                                      connections:
                                                                                                      importedConnections,
                                                                                                      catalog: widgetCatalog),
                                                                                   }),
                                     ],
                                   });
    }

    // A file from before search engines gets the default ones. One engine is always the default.
    if (backup.SearchEngines == null)
    {
      this.context.SearchEngines.AddRange (DefaultSearchEngines.Create ());
    }
    else
    {
      bool hasDefault = backup.SearchEngines.Any (e => e.IsDefault);

      this.context.SearchEngines.AddRange (backup.SearchEngines.Select ((e, i) => new SearchEngine
                                                                                  {
                                                                                    Name        = e.Name!.Trim (),
                                                                                    UrlTemplate = e.UrlTemplate!.Trim (),
                                                                                    Shortcut =
                                                                                      DashboardBackupService
                                                                                       .NormalizeShortcut (e.Shortcut),
                                                                                    IsDefault =
                                                                                      e.IsDefault || (!hasDefault && (i == 0)),
                                                                                    SortOrder = i,
                                                                                  }));
    }

    // A file from before panels gets the default ones. Positions count within each zone.
    if (backup.Panels == null)
    {
      this.context.Panels.AddRange (DefaultPanels.Create ());
    }
    else
    {
      int top = 0, bottom = 0;

      this.context.Panels.AddRange (backup.Panels.Select (p => DashboardBackupService.ImportPanel (panel: p,
                                                                                                   sortOrder: p.Zone ==
                                                                                                              PanelZone.Top
                                                                                                                ? top++
                                                                                                                : bottom++,
                                                                                                   connections: importedConnections,
                                                                                                   catalog: widgetCatalog)));
    }

    await this.context.SaveChangesAsync (cancellationToken);

    BackupSettings    imported = backup.Settings!;
    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();

    settings.Title        = imported.Title!.Trim ();
    settings.Motto        = (imported.Motto        ?? "").Trim ();
    settings.LocalDomains = (imported.LocalDomains ?? "").Trim ();
    settings.ColorMode    = imported.ColorMode;

    await this.settingsService.SaveSettingsAsync (settings);
    await transaction.CommitAsync (cancellationToken);
    this.connections.ForgetCached ();
  }

  /// <summary>
  ///   Checks a dashboard file's contents, collecting up to ten problems.
  /// </summary>
  /// <param name="backup">The configuration.</param>
  /// <param name="kinds">The panel kinds the file's panels may use.</param>
  /// <param name="integrations">The integrations the file's connections may use.</param>
  /// <param name="errors">The list the problems are added to.</param>
  private static void Validate (DashboardBackup backup, PanelCatalog kinds, IntegrationCatalog integrations, List<string> errors)
  {
    void Add (string error)
    {
      if (errors.Count < DashboardBackupService.MaxErrors)
      {
        errors.Add (error);
      }
    }

    if (backup.Settings is not { } settings)
    {
      Add ("The file has no settings.");
    }
    else
    {
      if (string.IsNullOrWhiteSpace (settings.Title))
      {
        Add ("Settings: the title is required.");
      }

      if (!Enum.IsDefined (settings.ColorMode))
      {
        Add ("Settings: the color mode isn't one of Auto, Light or Dark.");
      }
    }

    if (backup.Categories == null)
    {
      Add ("The file has no categories list.");
    }

    var widgetCatalog = new WidgetCatalog (integrations);
    HashSet<(string, string)> fileConnections =
    [
      .. (backup.Connections ?? []).Where (c => !string.IsNullOrWhiteSpace (c.Integration) && !string.IsNullOrWhiteSpace (c.Name))
                                   .Select (c => (c.Integration!.Trim (), c.Name!.Trim ())),
    ];

    for (var c = 0; c < (backup.Categories?.Count ?? 0); c++)
    {
      BackupCategory category = backup.Categories![c];
      var            where    = $"Category {c + 1}";

      if (string.IsNullOrWhiteSpace (category.Name))
      {
        Add ($"{where}: the name is required.");
      }
      else
      {
        where += $" (\"{category.Name.Trim ()}\")";
      }

      for (var l = 0; l < (category.Links?.Count ?? 0); l++)
      {
        BackupLink link     = category.Links![l];
        var        linkName = $"{where}, link {l + 1}";

        if (string.IsNullOrWhiteSpace (link.Label))
        {
          Add ($"{linkName}: the label is required.");
        }

        if (string.IsNullOrWhiteSpace (link.Url) || !DashboardBackupService.IsWebUrl (link.Url))
        {
          Add ($"{linkName}: the URL must be an HTTP or HTTPS URL.");
        }

        if (!string.IsNullOrWhiteSpace (link.IconUrl) && !DashboardBackupService.IsWebUrl (link.IconUrl))
        {
          Add ($"{linkName}: the icon URL must be an HTTP or HTTPS URL.");
        }

        if (link.MonitorId is <= 0)
        {
          Add ($"{linkName}: the monitor ID must be a positive integer.");
        }

        foreach (string error in DashboardBackupService.CheckWidgets (widgets: link.Widgets,
                                                                      placement: WidgetPlacements.Tile,
                                                                      catalog: widgetCatalog,
                                                                      connections: fileConnections))
        {
          Add ($"{linkName}: {error}");
        }
      }
    }

    for (var g = 0; g < (backup.InfrastructureGroups?.Count ?? 0); g++)
    {
      BackupInfrastructureGroup group = backup.InfrastructureGroups![g];

      if (string.IsNullOrWhiteSpace (group.Name))
      {
        Add ($"Infrastructure group {g + 1}: the name is required.");
      }

      if (group.MonitorId <= 0)
      {
        Add ($"Infrastructure group {g + 1}: the monitor ID must be a positive integer.");
      }
    }

    if ((backup.Version >= DashboardBackup.PanelsVersion) && (backup.Panels == null))
    {
      Add ("The file has no panels list.");
    }

    if ((backup.Version >= DashboardBackup.SearchEnginesVersion) && (backup.SearchEngines == null))
    {
      Add ("The file has no search engines list.");
    }

    var seenShortcuts = new HashSet<string> (StringComparer.Ordinal);

    for (var e = 0; e < (backup.SearchEngines?.Count ?? 0); e++)
    {
      BackupSearchEngine engine = backup.SearchEngines![e];
      var                where  = $"Search engine {e + 1}";

      if (SearchEngineRules.CheckName ((engine.Name ?? "").Trim ()) is { } nameError)
      {
        Add ($"{where}: {DashboardBackupService.Lower (nameError)}");
      }
      else
      {
        where += $" (\"{engine.Name!.Trim ()}\")";
      }

      if (SearchEngineRules.CheckUrlTemplate ((engine.UrlTemplate ?? "").Trim ()) is { } templateError)
      {
        Add ($"{where}: {DashboardBackupService.Lower (templateError)}");
      }

      if (!SearchEngineRules.TryNormalizeShortcut (value: engine.Shortcut, shortcut: out string shortcut))
      {
        Add ($"{where}: {DashboardBackupService.Lower (SearchEngineRules.InvalidShortcut)}");
      }
      else if ((shortcut.Length > 0) && !seenShortcuts.Add (shortcut))
      {
        Add ($"{where}: another engine already uses !{shortcut}.");
      }
    }

    if ((backup.SearchEngines?.Count (e => e.IsDefault) ?? 0) > 1)
    {
      Add ("Only one search engine can be the default.");
    }

    var seenKinds = new HashSet<string> (StringComparer.Ordinal);

    for (var p = 0; p < (backup.Panels?.Count ?? 0); p++)
    {
      BackupPanel panel = backup.Panels![p];
      var         where = $"Panel {p + 1}";

      if (string.IsNullOrWhiteSpace (panel.Kind))
      {
        Add ($"{where}: the kind is required.");
      }
      else if (kinds.Find (panel.Kind.Trim ()) is not { } kind)
      {
        Add ($"{where}: h2s has no “{panel.Kind.Trim ()}” panel kind.");
      }
      else if (!seenKinds.Add (kind.Key) && kind.IsSingleInstance)
      {
        Add ($"{where}: the dashboard can only have one {kind.DisplayName.ToLowerInvariant ()} panel.");
      }

      if (!Enum.IsDefined (panel.Zone))
      {
        Add ($"{where}: the zone isn't one of Top or Bottom.");
      }

      if (panel.Options is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) })
      {
        Add ($"{where}: the options must be a JSON object.");
      }

      WidgetPlacements placement = kinds.Find ((panel.Kind ?? "").Trim ())?.WidgetPlacement ?? WidgetPlacements.None;

      if ((placement == WidgetPlacements.None) && panel.Widgets is { Count: > 0 })
      {
        Add ($"{where}: this kind of panel holds no widgets.");
      }
      else
      {
        foreach (string error in DashboardBackupService.CheckWidgets (widgets: panel.Widgets,
                                                                      placement: placement,
                                                                      catalog: widgetCatalog,
                                                                      connections: fileConnections))
        {
          Add ($"{where}, {error}");
        }
      }
    }

    if ((backup.Version >= DashboardBackup.ConnectionsVersion) && (backup.Connections == null))
    {
      Add ("The file has no connections list.");
    }

    var seenIntegrations = new HashSet<string> (StringComparer.Ordinal);
    var seenNames        = new HashSet<(string, string)> ();

    for (var c = 0; c < (backup.Connections?.Count ?? 0); c++)
    {
      BackupConnection connection = backup.Connections![c];
      var              where      = $"Connection {c + 1}";
      string           name       = (connection.Name ?? "").Trim ();

      if (name.Length == 0)
      {
        Add ($"{where}: the name is required.");
      }
      else if (name.Length > Connection.MaxNameLength)
      {
        Add ($"{where}: the name must be at most {Connection.MaxNameLength} characters.");
      }
      else
      {
        where += $" (\"{name}\")";
      }

      if (string.IsNullOrWhiteSpace (connection.Integration))
      {
        Add ($"{where}: the integration is required.");
      }
      else if (integrations.Find (connection.Integration.Trim ()) is not { } integration)
      {
        Add ($"{where}: h2s has no “{connection.Integration.Trim ()}” integration.");
      }
      else
      {
        if (!seenIntegrations.Add (integration.Key) && !integration.AllowsMultipleConnections)
        {
          Add ($"{where}: the dashboard can only have one {integration.DisplayName} connection.");
        }
        else if ((name.Length > 0) && !seenNames.Add ((integration.Key, name)))
        {
          Add ($"{where}: another {integration.DisplayName} connection has that name.");
        }

        if (connection.Options is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) })
        {
          Add ($"{where}: the options must be a JSON object.");
        }
        else if (!DashboardBackupService.TryReadConnectionOptions (integration: integration,
                                                                   options: connection.Options,
                                                                   json: out _,
                                                                   error: out string? error))
        {
          Add ($"{where}: {error}");
        }
      }
    }
  }

  /// <summary>
  ///   Turns the Uptime Kuma settings of a file from before connections into a connection.
  /// </summary>
  /// <param name="settings">The file's settings.</param>
  /// <returns>The connections: one when the settings have an Uptime Kuma server, and none otherwise.</returns>
  private static List<BackupConnection> ConnectionsFromSettings (BackupSettings? settings)
  {
    if (string.IsNullOrWhiteSpace (settings?.UptimeKumaServerUrl))
    {
      return [];
    }

    var options = new Dictionary<string, object?>
                  {
                    [UptimeKumaIntegration.ServerUrlOption]            = settings.UptimeKumaServerUrl.Trim (),
                    [UptimeKumaIntegration.LegacyStatusPageSlugOption] = (settings.UptimeKumaStatusPageSlug ?? "").Trim (),
                  };

    if (settings.UptimeKumaDefaultDuration is { } duration)
    {
      options[UptimeKumaIntegration.LegacyDefaultDurationOption] = duration;
    }

    return
    [
      new BackupConnection (Integration: UptimeKumaIntegration.IntegrationKey,
                            Name: "Uptime Kuma",
                            Options: JsonSerializer.SerializeToElement (options)),
    ];
  }

  /// <summary>
  ///   Turns the uptime periods in hours of a file from before uptime windows into windows: the Uptime Kuma connection's
  ///   default, and the status bar panel's.
  /// </summary>
  /// <param name="backup">The file.</param>
  /// <returns>The file with windows.</returns>
  private static DashboardBackup UseUptimeWindows (DashboardBackup backup)
    => backup with
       {
         Connections = backup.Connections
                            ?.Select (c => c.Integration?.Trim () == UptimeKumaIntegration.IntegrationKey
                                             ? c with
                                               {
                                                 Options = DashboardBackupService.HoursToWindow (options: c.Options,
                                                                                                 hoursKey: UptimeKumaIntegration
                                                                                                  .LegacyDefaultDurationOption,
                                                                                                 windowKey: UptimeKumaIntegration
                                                                                                  .DefaultUptimeWindowOption),
                                               }
                                             : c)
                             .ToList (),
         Panels = backup.Panels
                       ?.Select (p => p.Kind?.Trim () == StatusBarPanelKind.KindKey
                                        ? p with
                                          {
                                            Options = DashboardBackupService.HoursToWindow (options: p.Options,
                                                                                            hoursKey: StatusBarPanelKind
                                                                                             .LegacyUptimeHoursOption,
                                                                                            windowKey: StatusBarPanelKind
                                                                                             .LegacyUptimeWindowOption),
                                          }
                                        : p)
                        .ToList (),
       };

  /// <summary>
  ///   Turns a file from before status bar widgets into one with them, as the ConvertGroupsToStatusBarWidgets migration
  ///   does. Its first status bar (the default one, for a file from before panels) gets an Uptime Kuma monitor widget for
  ///   each infrastructure group, labelled with the group's name, with the bar's old uptime settings and no response time,
  ///   using the file's first Uptime Kuma connection; and that connection's status page becomes the bar's link. Without
  ///   an Uptime Kuma connection the groups are dropped. Groups are left in the file for validation, which still refuses
  ///   one without a name or a positive monitor ID, and import ignores them.
  /// </summary>
  /// <param name="backup">The file.</param>
  /// <returns>The file with status bar widgets.</returns>
  private static DashboardBackup UseStatusBarWidgets (DashboardBackup backup)
  {
    BackupConnection?               kuma   = backup.Connections?.FirstOrDefault (DashboardBackupService.IsUptimeKuma);
    List<BackupInfrastructureGroup> groups = backup.InfrastructureGroups ?? [];

    // A file from before panels gets the default ones on import; the groups need its status bar now.
    List<BackupPanel>? panels = backup.Panels;

    if ((panels == null) && (groups.Count > 0) && (kuma != null))
    {
      panels = [.. DefaultPanels.Create ().Select (p => new BackupPanel (Kind: p.Kind, Zone: p.Zone, Options: null))];
    }

    int first = panels?.FindIndex (p => p.Kind?.Trim () == StatusBarPanelKind.KindKey) ?? -1;

    return backup with
           {
             Panels = panels?.Select ((p, i) => DashboardBackupService.UseStatusBarWidgets (panel: p,
                                                                                            kuma: i == first ? kuma : null,
                                                                                            groups: groups))
                             .ToList (),

             // The slug is the bar's link now, so the connection doesn't keep it.
             Connections = backup.Connections?.Select (DashboardBackupService.WithoutStatusPageSlug).ToList (),
           };
  }

  /// <summary>
  ///   Turns one panel of a file from before status bar widgets into one with them (see
  ///   <see cref="UseStatusBarWidgets (DashboardBackup)" />): a status bar loses its old uptime options, and the first gets
  ///   the groups and the status page.
  /// </summary>
  /// <param name="panel">The panel.</param>
  /// <param name="kuma">The file's Uptime Kuma connection, for the first status bar; <c>null</c> otherwise.</param>
  /// <param name="groups">The file's infrastructure groups.</param>
  /// <returns>The panel.</returns>
  private static BackupPanel UseStatusBarWidgets (BackupPanel panel, BackupConnection? kuma, List<BackupInfrastructureGroup> groups)
  {
    if ((panel.Kind?.Trim () != StatusBarPanelKind.KindKey) ||
        panel.Options is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) })
    {
      return panel;
    }

    JsonObject options = DashboardBackupService.ToJsonObject (panel.Options) ?? [];
    bool showUptime = DashboardBackupService.ReadBoolean (options: options, key: StatusBarPanelKind.LegacyShowUptimeOption) ?? true;
    string window = DashboardBackupService.ReadText (options: options, key: StatusBarPanelKind.LegacyUptimeWindowOption) ?? "";

    _ = options.Remove (StatusBarPanelKind.LegacyShowUptimeOption);
    _ = options.Remove (StatusBarPanelKind.LegacyUptimeWindowOption);
    _ = options.Remove (StatusBarPanelKind.LegacyUptimeHoursOption);

    if (kuma == null)
    {
      return panel with { Options = JsonSerializer.SerializeToElement (options) };
    }

    JsonObject kumaOptions = DashboardBackupService.ToJsonObject (kuma.Options) ?? [];
    string     serverUrl = DashboardBackupService.ReadText (options: kumaOptions, key: UptimeKumaIntegration.ServerUrlOption) ?? "";
    string?    slug = DashboardBackupService.ReadText (options: kumaOptions, key: UptimeKumaIntegration.LegacyStatusPageSlugOption);

    if (StatusBarPanelKind.BuildStatusPageUrl (serverUrl: serverUrl, slug: slug) is { } link)
    {
      options[StatusBarPanelKind.LinkUrlOption] = link;
    }

    return panel with
           {
             Options = JsonSerializer.SerializeToElement (options),
             Widgets =
             [
               .. groups.Where (g => (g.MonitorId > 0) && !string.IsNullOrWhiteSpace (g.Name))
                        .Select (g => DashboardBackupService.GroupItem (group: g,
                                                                        connection: kuma.Name?.Trim (),
                                                                        showUptime: showUptime,
                                                                        window: window)),
             ],
           };
  }

  /// <summary>
  ///   Makes an infrastructure group a status bar item: an Uptime Kuma monitor widget labelled with its name.
  /// </summary>
  /// <param name="group">The group.</param>
  /// <param name="connection">The name of the file's Uptime Kuma connection.</param>
  /// <param name="showUptime">Whether the status bar showed uptime.</param>
  /// <param name="window">The status bar's uptime window; blank for the connection's.</param>
  /// <returns>The item.</returns>
  private static BackupWidget GroupItem (BackupInfrastructureGroup group, string? connection, bool showUptime, string window)
  {
    var options = new Dictionary<string, object>
                  {
                    [KumaMonitorWidgetKind.MonitorIdOption]        = group.MonitorId,
                    [KumaMonitorWidgetKind.ShowResponseTimeOption] = false,
                    [KumaMonitorWidgetKind.ShowUptimeOption]       = showUptime,
                    [KumaMonitorWidgetKind.UptimeWindowOption]     = window,
                  };

    return new BackupWidget (Kind: $"{UptimeKumaIntegration.IntegrationKey}/{KumaMonitorWidgetKind.KindKey}",
                             Connection: connection,
                             Options: JsonSerializer.SerializeToElement (options),
                             Label: group.Name!.Trim ());
  }

  /// <summary>
  ///   Takes the status page slug out of an Uptime Kuma connection in a dashboard file.
  /// </summary>
  /// <param name="connection">The connection.</param>
  /// <returns>The connection, without the slug when it's Uptime Kuma's and had one.</returns>
  private static BackupConnection WithoutStatusPageSlug (BackupConnection connection)
    => DashboardBackupService.IsUptimeKuma (connection)                        &&
       DashboardBackupService.ToJsonObject (connection.Options) is { } options &&
       options.Remove (UptimeKumaIntegration.LegacyStatusPageSlugOption)
         ? connection with { Options = JsonSerializer.SerializeToElement (options) }
         : connection;

  /// <summary>
  ///   Gets whether a connection in a dashboard file is Uptime Kuma's.
  /// </summary>
  /// <param name="connection">The connection.</param>
  /// <returns><c>true</c> when it's an Uptime Kuma connection.</returns>
  private static bool IsUptimeKuma (BackupConnection connection)
    => connection.Integration?.Trim () == UptimeKumaIntegration.IntegrationKey;

  /// <summary>
  ///   Reads a boolean from an options object in a dashboard file.
  /// </summary>
  /// <param name="options">The options.</param>
  /// <param name="key">The option's key.</param>
  /// <returns>The value, or <c>null</c> when it's missing or not a boolean.</returns>
  private static bool? ReadBoolean (JsonObject options, string key)
    => options[key] is JsonValue value && value.TryGetValue (out bool result) ? result : null;

  /// <summary>
  ///   Reads text from an options object in a dashboard file, trimmed.
  /// </summary>
  /// <param name="options">The options.</param>
  /// <param name="key">The option's key.</param>
  /// <returns>The value, or <c>null</c> when it's missing or not text.</returns>
  private static string? ReadText (JsonObject options, string key)
    => options[key] is JsonValue value && value.TryGetValue (out string? result) ? result.Trim () : null;

  /// <summary>
  ///   Reads an options object from a dashboard file as a mutable copy.
  /// </summary>
  /// <param name="options">The options, if any.</param>
  /// <returns>A copy, or <c>null</c> when they aren't a JSON object.</returns>
  private static JsonObject? ToJsonObject (JsonElement? options)
    => options is { ValueKind: JsonValueKind.Object } element ? JsonNode.Parse (element.GetRawText ()) as JsonObject : null;

  /// <summary>
  ///   Turns the links' Uptime Kuma monitor IDs of a file from before monitor widgets into monitor widgets, as the
  ///   ConvertLinkMonitorsToWidgets migration does: first among the link's widgets, with the kind's defaults, using the
  ///   file's first Uptime Kuma connection. A link that already has a monitor widget for that monitor, or a full set of
  ///   widgets, gets none, and without an Uptime Kuma connection the monitor IDs are dropped. A monitor ID that isn't
  ///   positive is left for validation to refuse.
  /// </summary>
  /// <param name="backup">The file.</param>
  /// <returns>The file with monitor widgets and no monitor IDs.</returns>
  private static DashboardBackup UseMonitorWidgets (DashboardBackup backup)
  {
    string? kuma = backup.Connections
                        ?.FirstOrDefault (c => c.Integration?.Trim () == UptimeKumaIntegration.IntegrationKey)
                        ?.Name
                        ?.Trim ();

    return backup with
           {
             Categories = backup.Categories
                               ?.Select (c => c with
                                              {
                                                Links = c.Links
                                                        ?.Select (l => DashboardBackupService.UseMonitorWidget (link: l,
                                                                                                                connection: kuma))
                                                         .ToList (),
                                              })
                                .ToList (),
           };
  }

  /// <summary>
  ///   Turns one link's Uptime Kuma monitor ID into a monitor widget (see <see cref="UseMonitorWidgets" />).
  /// </summary>
  /// <param name="link">The link.</param>
  /// <param name="connection">The name of the file's Uptime Kuma connection, if it has one.</param>
  /// <returns>The link, without its monitor ID.</returns>
  private static BackupLink UseMonitorWidget (BackupLink link, string? connection)
  {
    if (link.MonitorId is not ({ } monitorId and > 0))
    {
      return link;
    }

    List<BackupWidget> widgets = link.Widgets ?? [];
    var                kind    = $"{UptimeKumaIntegration.IntegrationKey}/{KumaMonitorWidgetKind.KindKey}";

    if ((connection    == null)                         ||
        (widgets.Count >= WidgetForm.MaxWidgetsPerLink) ||
        widgets.Any (w => (w.Kind?.Trim () == kind) && (DashboardBackupService.MonitorIdOf (w.Options) == monitorId)))
    {
      return link with { MonitorId = null };
    }

    BackupWidget widget = new (Kind: kind,
                               Connection: connection,
                               Options: JsonSerializer.SerializeToElement (new Dictionary<string, object>
                                                                           {
                                                                             [KumaMonitorWidgetKind.MonitorIdOption] = monitorId,
                                                                             [KumaMonitorWidgetKind.ShowResponseTimeOption] = true,
                                                                             [KumaMonitorWidgetKind.ShowUptimeOption] = true,
                                                                             [KumaMonitorWidgetKind.UptimeWindowOption] = "",
                                                                           }));

    return link with { MonitorId = null, Widgets = [widget, .. widgets] };
  }

  /// <summary>
  ///   Reads the monitor ID from a monitor widget's options in a dashboard file, stored as a number or as text.
  /// </summary>
  /// <param name="options">The widget's options.</param>
  /// <returns>The monitor ID, or <c>null</c> when there isn't one.</returns>
  private static int? MonitorIdOf (JsonElement? options)
  {
    if (options is not { ValueKind: JsonValueKind.Object } element ||
        !element.TryGetProperty (propertyName: KumaMonitorWidgetKind.MonitorIdOption, value: out JsonElement value))
    {
      return null;
    }

    return value.ValueKind switch
           {
             JsonValueKind.Number when value.TryGetInt32 (out int number) => number,
             JsonValueKind.String when int.TryParse (s: value.GetString ()?.Trim (),
                                                     style: NumberStyles.None,
                                                     provider: CultureInfo.InvariantCulture,
                                                     result: out int text) => text,
             _ => null,
           };
  }

  /// <summary>
  ///   Replaces a period in hours in an options object with the shortest uptime window that covers it.
  /// </summary>
  /// <param name="options">The options, if they're an object.</param>
  /// <param name="hoursKey">The key of the period in hours.</param>
  /// <param name="windowKey">The key of the window.</param>
  /// <returns>The options, with the window instead of the hours when there were hours.</returns>
  private static JsonElement? HoursToWindow (JsonElement? options, string hoursKey, string windowKey)
  {
    if (options is not { ValueKind: JsonValueKind.Object } element || JsonNode.Parse (element.GetRawText ()) is not JsonObject json)
    {
      return options;
    }

    if (json[hoursKey] is JsonValue hours && hours.TryGetValue (out int value))
    {
      json[windowKey] = UptimeKumaWindows.FromHours (value);
    }

    _ = json.Remove (hoursKey);

    return JsonSerializer.SerializeToElement (json);
  }

  /// <summary>
  ///   Describes a link for a dashboard file.
  /// </summary>
  /// <param name="link">The link, with its widgets loaded.</param>
  /// <param name="connections">The connections, by ID.</param>
  /// <returns>The link as the file holds it.</returns>
  private static BackupLink ExportLink (Link link, IReadOnlyDictionary<int, Connection> connections)
    => new (Label: link.Label,
            Url: link.Url,
            Description: link.Description,
            IconName: link.IconName,
            IconUrl: link.IconUrl,
            OpenInNewTab: link.OpenInNewTab,
            Widgets: DashboardBackupService.ExportWidgets (widgets: link.Widgets, connections: connections));

  /// <summary>
  ///   Creates a panel, with its widgets, from a checked dashboard file.
  /// </summary>
  /// <param name="panel">The panel in the file.</param>
  /// <param name="sortOrder">Its position within its zone.</param>
  /// <param name="connections">The connections being imported, by integration and name.</param>
  /// <param name="catalog">The known widget kinds.</param>
  /// <returns>The panel.</returns>
  private static Panel ImportPanel (BackupPanel                                                           panel,
                                    int                                                                   sortOrder,
                                    IReadOnlyDictionary<(string IntegrationKey, string Name), Connection> connections,
                                    WidgetCatalog                                                         catalog)
    => new ()
       {
         Kind      = panel.Kind!.Trim (),
         Zone      = panel.Zone,
         SortOrder = sortOrder,

         // Stored compact, however the file was indented.
         OptionsJson = panel.Options is { ValueKind: JsonValueKind.Object } options ? JsonSerializer.Serialize (options) : "{}",
         Widgets     = DashboardBackupService.ImportWidgets (widgets: panel.Widgets, connections: connections, catalog: catalog),
       };

  /// <summary>
  ///   Describes a panel for a dashboard file.
  /// </summary>
  /// <param name="panel">The panel, with its widgets loaded.</param>
  /// <param name="connections">The connections, by ID.</param>
  /// <returns>The panel as the file holds it.</returns>
  private static BackupPanel ExportPanel (Panel panel, IReadOnlyDictionary<int, Connection> connections)
    => new (Kind: panel.Kind,
            Zone: panel.Zone,
            Options: DashboardBackupService.ReadOptions (panel.OptionsJson),
            Widgets: DashboardBackupService.ExportWidgets (widgets: panel.Widgets, connections: connections));

  /// <summary>
  ///   Describes a link's or panel's widgets for a dashboard file, naming each one's connection.
  /// </summary>
  /// <param name="widgets">The widgets.</param>
  /// <param name="connections">The connections, by ID.</param>
  /// <returns>The widgets, in order; <c>null</c> when there are none.</returns>
  private static List<BackupWidget>? ExportWidgets (IEnumerable<Widget> widgets, IReadOnlyDictionary<int, Connection> connections)
  {
    List<BackupWidget> exported =
    [
      .. widgets
        .OrderBy (w => w.SortOrder)
        .ThenBy (w => w.Id)
        .Where (w => connections.ContainsKey (w.ConnectionId))
        .Select (w => new BackupWidget (Kind: WidgetKindEntry.ReferenceOf (integrationKey: connections[w.ConnectionId]
                                                                            .IntegrationKey,
                                                                           kindKey: w.Kind),
                                        Connection: connections[w.ConnectionId].Name,
                                        Options: DashboardBackupService.ReadOptions (w.OptionsJson),
                                        RefreshSeconds: w.RefreshSeconds,
                                        Label: w.Label)),
    ];

    return exported.Count == 0 ? null : exported;
  }

  /// <summary>
  ///   Creates a link's or panel's widgets from a checked dashboard file.
  /// </summary>
  /// <param name="widgets">The widgets in the file; none when <c>null</c>.</param>
  /// <param name="connections">The connections being imported, by integration and name.</param>
  /// <param name="catalog">The known widget kinds.</param>
  /// <returns>The widgets, in order.</returns>
  private static List<Widget> ImportWidgets (List<BackupWidget>?                                                   widgets,
                                             IReadOnlyDictionary<(string IntegrationKey, string Name), Connection> connections,
                                             WidgetCatalog                                                         catalog)
    =>
    [
      .. (widgets ?? []).Select ((w, i) =>
                                 {
                                   WidgetKindEntry entry = catalog.Find (w.Kind!)!;

                                   _ = DashboardBackupService.TryReadOptions (schema: entry.Kind.Options,
                                                                              options: w.Options,
                                                                              json: out string? json,
                                                                              error: out _);

                                   return new Widget
                                          {
                                            Connection     = connections[(entry.Integration.Key, w.Connection!.Trim ())],
                                            Kind           = entry.Kind.Key,
                                            OptionsJson    = json!,
                                            RefreshSeconds = w.RefreshSeconds,
                                            Label          = string.IsNullOrWhiteSpace (w.Label) ? null : w.Label.Trim (),
                                            SortOrder      = i,
                                          };
                                 }),
    ];

  /// <summary>
  ///   Checks a link's or panel's widgets in a dashboard file.
  /// </summary>
  /// <param name="widgets">The widgets in the file; none when <c>null</c>.</param>
  /// <param name="placement">Where they go: a tile or a status bar.</param>
  /// <param name="catalog">The known widget kinds.</param>
  /// <param name="connections">The file's connections, by integration and name.</param>
  /// <returns>What's wrong with them, one problem each.</returns>
  private static IEnumerable<string> CheckWidgets (List<BackupWidget>?            widgets,
                                                   WidgetPlacements               placement,
                                                   WidgetCatalog                  catalog,
                                                   IReadOnlySet<(string, string)> connections)
  {
    widgets ??= [];

    if (widgets.Count > WidgetForm.MaxWidgets (placement))
    {
      yield return $"{WidgetForm.Describe (placement)} can have at most {WidgetForm.MaxWidgets (placement)} widgets.";
    }

    var apps = 0;

    for (var i = 0; i < widgets.Count; i++)
    {
      BackupWidget widget = widgets[i];
      var          where  = $"widget {i + 1}";

      if (catalog.Find (widget.Kind ?? "") is not { } entry)
      {
        yield return $"{where}: h2s has no “{widget.Kind}” kind of widget.";

        continue;
      }

      if (!entry.Kind.Placements.HasFlag (placement))
      {
        yield return $"{where}: this kind of widget can't go on {WidgetForm.Describe (placement)}.";
      }

      if (string.IsNullOrWhiteSpace (widget.Connection) ||
          !connections.Contains ((entry.Integration.Key, widget.Connection.Trim ())))
      {
        yield return $"{where}: the file has no {entry.Integration.DisplayName} connection called “{widget.Connection}”.";
      }

      if ((entry.Kind.Role == WidgetRole.App) && (++apps > 1))
      {
        yield return $"{where}: {WidgetForm.Describe (placement)} can have only one app widget.";
      }

      if (widget.Options is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) })
      {
        yield return $"{where}: the options must be a JSON object.";
      }
      else if (!DashboardBackupService.TryReadOptions (schema: entry.Kind.Options,
                                                       options: widget.Options,
                                                       json: out _,
                                                       error: out string? error))
      {
        yield return $"{where}: {error}";
      }

      if (widget.Label?.Trim ().Length > Widget.MaxLabelLength)
      {
        yield return $"{where}: the label can be at most {Widget.MaxLabelLength} characters.";
      }

      if (widget.RefreshSeconds is < Widget.MinRefreshSeconds or > Widget.MaxRefreshSeconds)
      {
        yield return
          $"{where}: the refresh interval must be from {Widget.MinRefreshSeconds} to {Widget.MaxRefreshSeconds} seconds.";
      }
    }
  }

  /// <summary>
  ///   Checks an imported connection's settings against its integration's schema and rules, as Settings checks what it
  ///   posts. A setting the file leaves out takes its default.
  /// </summary>
  /// <param name="integration">The connection's integration.</param>
  /// <param name="options">The settings in the file: a JSON object, or <c>null</c> for all defaults.</param>
  /// <param name="json">The settings as the connection stores them, when they're valid.</param>
  /// <param name="error">What's wrong with them, when they aren't.</param>
  /// <returns><c>true</c> when the settings are valid.</returns>
  private static bool TryReadConnectionOptions (IIntegration                      integration,
                                                JsonElement?                      options,
                                                [NotNullWhen (true)] out  string? json,
                                                [NotNullWhen (false)] out string? error)
  {
    if (!DashboardBackupService.TryReadOptions (schema: integration.ConnectionOptions,
                                                options: options,
                                                json: out json,
                                                error: out error))
    {
      return false;
    }

    error = integration.Validate (new OptionValues (schema: integration.ConnectionOptions, json: json));

    if (error == null)
    {
      return true;
    }

    json = null;

    return false;
  }

  /// <summary>
  ///   Checks options from a dashboard file against a schema, as an edit form's values are checked. An option the file
  ///   leaves out takes its default.
  /// </summary>
  /// <param name="schema">The options.</param>
  /// <param name="options">The values in the file: a JSON object, or <c>null</c> for all defaults.</param>
  /// <param name="json">The values as stored, when they're valid.</param>
  /// <param name="error">What's wrong with them, when they aren't.</param>
  /// <returns><c>true</c> when the values are valid.</returns>
  private static bool TryReadOptions (IReadOnlyList<SchemaOption>       schema,
                                      JsonElement?                      options,
                                      [NotNullWhen (true)] out  string? json,
                                      [NotNullWhen (false)] out string? error)
  {
    var posted = new Dictionary<string, string?> (StringComparer.Ordinal);

    foreach (SchemaOption option in schema)
    {
      posted[option.Key] =
        options is { ValueKind: JsonValueKind.Object } values &&
        values.TryGetProperty (propertyName: option.Key, value: out JsonElement value)
          ? value.ValueKind switch
            {
              JsonValueKind.String => value.GetString (),
              JsonValueKind.True   => "true",
              JsonValueKind.False  => "false",
              JsonValueKind.Null   => "",
              _                    => value.GetRawText (),
            }
          : option.DefaultValue switch
            {
              bool flag  => flag ? "true" : "false",
              int number => number.ToString (CultureInfo.InvariantCulture),
              _          => option.DefaultValue as string,
            };
    }

    return OptionForm.TryParse (schema: schema, posted: posted, json: out json, error: out error);
  }

  /// <summary>
  ///   Normalizes an imported shortcut, already checked by <see cref="TryRead" />.
  /// </summary>
  /// <param name="shortcut">The shortcut in the file.</param>
  /// <returns>The shortcut as stored: lowercase, without its <c>!</c>.</returns>
  private static string NormalizeShortcut (string? shortcut)
  {
    _ = SearchEngineRules.TryNormalizeShortcut (value: shortcut, shortcut: out string normalized);

    return normalized;
  }

  /// <summary>
  ///   Lowercases a message's first letter, to follow a "Search engine 1:" prefix.
  /// </summary>
  /// <param name="message">The message.</param>
  /// <returns>The message, starting lowercase.</returns>
  private static string Lower (string message) => message.Length == 0 ? message : char.ToLowerInvariant (message[0]) + message[1..];

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
      {
        return document.RootElement.Clone ();
      }
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

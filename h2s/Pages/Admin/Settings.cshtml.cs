using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using h2s.Data;
using h2s.Integrations;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Options;
using h2s.Panels;
using h2s.Services;
using h2s.Validation;
using h2s.Widgets.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages.Admin;

/// <summary>
///   Page model for editing the singleton dashboard settings record.
/// </summary>
public partial class SettingsModel : PageModel
{
  // A dashboard file is a few kilobytes per hundred links, so this is generous.
  private const long MaxBackupBytes = 2 * 1024 * 1024;

  private readonly DashboardBackupService   backupService;
  private readonly ConnectionService        connections;
  private readonly DashboardContext         context;
  private readonly IntegrationCatalog       integrations;
  private readonly PanelCatalog             panelKinds;
  private readonly DashboardSettingsService settingsService;

  /// <summary>
  ///   Initializes a new instance of the <see cref="SettingsModel" /> class.
  /// </summary>
  /// <param name="settingsService">The service used to load and save dashboard settings.</param>
  /// <param name="context">The database context used to manage infrastructure groups.</param>
  /// <param name="backupService">The service used to export and import the whole configuration.</param>
  /// <param name="panelKinds">The panel kinds an imported file's panels may use.</param>
  /// <param name="integrations">The integrations whose connections the page edits and tests.</param>
  /// <param name="connections">The service holding the connections.</param>
  public SettingsModel (DashboardSettingsService settingsService,
                        DashboardContext         context,
                        DashboardBackupService   backupService,
                        PanelCatalog             panelKinds,
                        IntegrationCatalog       integrations,
                        ConnectionService        connections)
  {
    this.panelKinds      = panelKinds;
    this.settingsService = settingsService;
    this.context         = context;
    this.backupService   = backupService;
    this.integrations    = integrations;
    this.connections     = connections;
  }

  /// <summary>
  ///   Gets the settings the page shows.
  /// </summary>
  public DashboardSettings Settings { get; private set; } = new ();

  /// <summary>
  ///   Gets the infrastructure groups configured for the status bar.
  /// </summary>
  public List<InfrastructureGroup> InfrastructureGroups { get; private set; } = new ();

  /// <summary>
  ///   Gets the Uptime Kuma connection's settings, or <c>null</c> when Uptime Kuma is off.
  /// </summary>
  public UptimeKumaSettings? UptimeKuma { get; private set; }

  /// <summary>
  ///   Gets a value indicating whether Uptime Kuma is configured, which shows the infrastructure groups.
  /// </summary>
  public bool IsUptimeKumaConfigured => this.UptimeKuma != null;

  /// <summary>
  ///   Gets the Uptime Kuma integration.
  /// </summary>
  private IIntegration KumaIntegration => this.integrations.Find (UptimeKumaIntegration.IntegrationKey)!;

  /// <summary>
  ///   Downloads the whole configuration as a dashboard file.
  /// </summary>
  /// <returns>The file, named for the time of export.</returns>
  public async Task<IActionResult> OnGetExportAsync ()
  {
    DashboardBackup backup = await this.backupService.ExportAsync (this.HttpContext.RequestAborted);
    byte[]          json   = JsonSerializer.SerializeToUtf8Bytes (value: backup, options: DashboardBackup.JsonOptions);

    return this.File (fileContents: json,
                      contentType: "application/json",
                      fileDownloadName: $"h2s-dashboard-{backup.ExportedAt:yyyyMMdd-HHmmss}.json");
  }

  /// <summary>
  ///   Checks an uploaded dashboard file and summarizes what importing it would bring in, without changing anything.
  /// </summary>
  /// <param name="file">The dashboard file.</param>
  /// <returns>
  ///   A JSON payload with what the file holds and what the dashboard holds now, or a 400 problem response listing what
  ///   is wrong with the file.
  /// </returns>
  public async Task<IActionResult> OnPostPreviewImportAsync (IFormFile? file)
  {
    (DashboardBackup? backup, IActionResult? problem) = await this.ReadBackupAsync (file);

    if (backup == null)
      return problem!;

    return new JsonResult (new
                           {
                             Incoming = DashboardBackupService.Summarize (backup),
                             Current = new
                                       {
                                         Categories           = await this.context.Categories.CountAsync (),
                                         Links                = await this.context.Links.CountAsync (),
                                         InfrastructureGroups = await this.context.InfrastructureGroups.CountAsync (),
                                         Panels               = await this.context.Panels.CountAsync (),
                                         SearchEngines        = await this.context.SearchEngines.CountAsync (),
                                         Connections          = await this.context.Connections.CountAsync (),
                                       },
                           });
  }

  /// <summary>
  ///   Replaces the whole configuration (settings, categories, links, infrastructure groups, panels, search engines and
  ///   connections) with an uploaded dashboard file's.
  /// </summary>
  /// <param name="file">The dashboard file.</param>
  /// <returns>A JSON payload summarizing what was imported, or a 400 problem response listing what is wrong with the file.</returns>
  public async Task<IActionResult> OnPostImportAsync (IFormFile? file)
  {
    (DashboardBackup? backup, IActionResult? problem) = await this.ReadBackupAsync (file);

    if (backup == null)
      return problem!;

    await this.backupService.ImportAsync (backup: backup, cancellationToken: this.HttpContext.RequestAborted);

    return new JsonResult (DashboardBackupService.Summarize (backup));
  }

  /// <summary>
  ///   Loads the current dashboard settings for display.
  /// </summary>
  public async Task OnGetAsync ()
  {
    this.Settings   = await this.settingsService.GetSettingsAsync ();
    this.UptimeKuma = await UptimeKumaSettings.LoadAsync (this.connections);
    this.InfrastructureGroups = await this.GetOrderedInfrastructureGroupsQuery ()
                                          .AsNoTracking ()
                                          .ToListAsync ();
    this.SearchEngines = await this.GetOrderedSearchEnginesAsync ();
  }

  /// <summary>
  ///   Saves the General section: the title, motto and local domains.
  /// </summary>
  /// <param name="title">The dashboard title; required.</param>
  /// <param name="motto">The motto under the title; blank for none.</param>
  /// <param name="localDomains">The local domains, separated by commas, semicolons, spaces or newlines.</param>
  /// <returns>A JSON payload with the saved values, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostSaveGeneralAsync (string? title, string? motto, string? localDomains)
  {
    string normalizedTitle = (title ?? string.Empty).Trim ();

    if (normalizedTitle.Length == 0)
      return this.BadRequestProblem ("Title is required.");

    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();
    settings.Title        = normalizedTitle;
    settings.Motto        = (motto        ?? string.Empty).Trim ();
    settings.LocalDomains = (localDomains ?? string.Empty).Trim ();
    await this.settingsService.SaveSettingsAsync (settings);

    return new JsonResult (new { settings.Title, settings.Motto, settings.LocalDomains });
  }

  /// <summary>
  ///   Saves the Appearance section: the color mode.
  /// </summary>
  /// <param name="colorMode">The color mode's name (<c>Auto</c>, <c>Light</c> or <c>Dark</c>) or number.</param>
  /// <returns>A JSON payload with the saved color mode's name, or a 400 problem response when it isn't one.</returns>
  public async Task<IActionResult> OnPostSaveAppearanceAsync (string? colorMode)
  {
    if (!Enum.TryParse (value: colorMode?.Trim (), ignoreCase: true, result: out ColorMode mode) || !Enum.IsDefined (mode))
      return this.BadRequestProblem ("Color mode must be Auto, Light or Dark.");

    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();
    settings.ColorMode = mode;
    await this.settingsService.SaveSettingsAsync (settings);

    return new JsonResult (new { ColorMode = mode.ToString () });
  }

  /// <summary>
  ///   Saves the Uptime Kuma part of the Integrations section, as the Uptime Kuma connection.
  /// </summary>
  /// <param name="serverUrl">The server's base URL; blank turns the integration off, removing the connection.</param>
  /// <param name="statusPageSlug">The status page slug; blank for none.</param>
  /// <param name="defaultDuration">The default uptime badge duration, in whole hours of at least 1.</param>
  /// <returns>A JSON payload with the saved values, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostSaveUptimeKumaAsync (string? serverUrl, string? statusPageSlug, string? defaultDuration)
  {
    CancellationToken cancellationToken = this.HttpContext.RequestAborted;

    if (SettingsModel.Blank (serverUrl))
    {
      await this.connections.DeleteAllAsync (integrationKey: this.KumaIntegration.Key, cancellationToken: cancellationToken);

      return new JsonResult (new
                             {
                               ServerUrl       = (string?)null,
                               StatusPageSlug  = SettingsModel.Blank (statusPageSlug) ? null : statusPageSlug!.Trim (),
                               DefaultDuration = defaultDuration?.Trim (),
                               IsConfigured    = false,
                             });
    }

    if (!this.TryReadUptimeKuma (serverUrl: serverUrl,
                                 statusPageSlug: statusPageSlug,
                                 defaultDuration: defaultDuration,
                                 json: out string? json,
                                 options: out OptionValues? options,
                                 problem: out IActionResult? problem))
      return problem;

    _ = await this.connections.SaveOnlyAsync (integration: this.KumaIntegration,
                                              optionsJson: json,
                                              cancellationToken: cancellationToken);
    UptimeKumaSettings saved = UptimeKumaSettings.From (options)!;

    return new JsonResult (new
                           {
                             saved.ServerUrl, saved.StatusPageSlug, saved.DefaultDuration, IsConfigured = true,
                           });
  }

  /// <summary>
  ///   Tests Uptime Kuma settings without saving them, through the integration's connection test.
  /// </summary>
  /// <param name="serverUrl">The server's base URL, as entered.</param>
  /// <param name="statusPageSlug">The status page slug, as entered; blank to test only the server.</param>
  /// <returns>
  ///   A JSON payload with a level (<c>success</c>, <c>warning</c> or <c>danger</c>) and a message, or a 400 problem
  ///   response when the URL is missing or malformed.
  /// </returns>
  public async Task<IActionResult> OnPostTestUptimeKumaAsync (string? serverUrl, string? statusPageSlug)
  {
    if (SettingsModel.Blank (serverUrl))
      return this.BadRequestProblem ("Enter the Uptime Kuma server URL to test.");

    // The test doesn't use the badge period, so the form doesn't post it.
    var duration = UptimeKumaIntegration.DefaultDuration.ToString (CultureInfo.InvariantCulture);

    if (!this.TryReadUptimeKuma (serverUrl: serverUrl,
                                 statusPageSlug: statusPageSlug,
                                 defaultDuration: duration,
                                 json: out _,
                                 options: out OptionValues? options,
                                 problem: out IActionResult? problem))
      return problem;

    ConnectionTestResult result =
      await this.KumaIntegration.TestAsync (new ConnectionContext (Options: options,
                                                                   Services: this.HttpContext.RequestServices,
                                                                   CancellationToken: this.HttpContext.RequestAborted));

    return new JsonResult (new { Level = result.Level.ToString ().ToLowerInvariant (), result.Message });
  }

  /// <summary>
  ///   Returns the ordered infrastructure group list used by the settings client script.
  /// </summary>
  /// <returns>A JSON payload describing all infrastructure groups.</returns>
  public async Task<IActionResult> OnGetInfrastructureGroupsAsync ()
  {
    var groups = await this.GetOrderedInfrastructureGroupsQuery ()
                           .AsNoTracking ()
                           .Select (g => new
                                         {
                                           g.Id, g.Name, g.MonitorId, g.SortOrder,
                                         })
                           .ToListAsync ();

    return new JsonResult (groups);
  }

  /// <summary>
  ///   Creates a new infrastructure group from posted values.
  /// </summary>
  /// <param name="name">The group display name.</param>
  /// <param name="monitorId">The monitor ID, which must be a positive integer.</param>
  /// <returns>A JSON payload describing the created group, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateInfrastructureGroupAsync (string? name, string? monitorId)
  {
    string normalizedName = (name ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
      return this.BadRequestProblem ("Group name is required.");

    if (!MonitorIdParser.TryParse (value: monitorId, monitorId: out int parsedMonitorId))
      return this.BadRequestProblem ("Monitor ID must be a positive integer.");

    int nextSortOrder = await this.context.InfrastructureGroups
                                  .Select (g => (int?)g.SortOrder)
                                  .MaxAsync () ??
                        -1;

    var group = new InfrastructureGroup { Name = normalizedName, MonitorId = parsedMonitorId, SortOrder = nextSortOrder + 1 };

    this.context.InfrastructureGroups.Add (group);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new
                           {
                             group.Id, group.Name, group.MonitorId, group.SortOrder,
                           });
  }

  /// <summary>
  ///   Updates an existing infrastructure group.
  /// </summary>
  /// <param name="id">The group identifier.</param>
  /// <param name="name">The updated display name.</param>
  /// <param name="monitorId">The updated monitor ID.</param>
  /// <returns>A JSON payload describing the updated group, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostUpdateInfrastructureGroupAsync (int id, string? name, string? monitorId)
  {
    string normalizedName = (name ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
      return this.BadRequestProblem ("Group name is required.");

    if (!MonitorIdParser.TryParse (value: monitorId, monitorId: out int parsedMonitorId))
      return this.BadRequestProblem ("Monitor ID must be a positive integer.");

    InfrastructureGroup? group = await this.context.InfrastructureGroups.FirstOrDefaultAsync (g => g.Id == id);

    if (group == null)
      return this.NotFoundProblem ("Infrastructure group not found.");

    group.Name      = normalizedName;
    group.MonitorId = parsedMonitorId;
    await this.context.SaveChangesAsync ();

    return new JsonResult (new
                           {
                             group.Id, group.Name, group.MonitorId, group.SortOrder,
                           });
  }

  /// <summary>
  ///   Deletes an infrastructure group.
  /// </summary>
  /// <param name="id">The identifier of the group to remove.</param>
  /// <returns>
  ///   A JSON payload with the deleted group's ID and what <see cref="OnPostRestoreInfrastructureGroupAsync" /> needs to put
  ///   it back, or a 404 problem response when it does not exist.
  /// </returns>
  public async Task<IActionResult> OnPostDeleteInfrastructureGroupAsync (int id)
  {
    InfrastructureGroup? group = await this.context.InfrastructureGroups.FirstOrDefaultAsync (g => g.Id == id);

    if (group == null)
      return this.NotFoundProblem ("Infrastructure group not found.");

    this.context.InfrastructureGroups.Remove (group);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id, Restore = new { group.Name, group.MonitorId, group.SortOrder } });
  }

  /// <summary>
  ///   Recreates a deleted infrastructure group at its old position, for Undo.
  /// </summary>
  /// <param name="name">The group name.</param>
  /// <param name="monitorId">The monitor ID.</param>
  /// <param name="sortOrder">The group's old position.</param>
  /// <returns>A JSON payload describing the recreated group, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostRestoreInfrastructureGroupAsync (string? name, string? monitorId, int sortOrder)
  {
    string normalizedName = (name ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
      return this.BadRequestProblem ("Group name is required.");

    if (!MonitorIdParser.TryParse (value: monitorId, monitorId: out int parsedMonitorId))
      return this.BadRequestProblem ("Monitor ID must be a positive integer.");

    int position = Math.Clamp (value: sortOrder, min: 0, max: await this.context.InfrastructureGroups.CountAsync ());

    _ = await this.context.InfrastructureGroups
                  .Where (g => g.SortOrder >= position)
                  .ExecuteUpdateAsync (s => s.SetProperty (propertyExpression: g => g.SortOrder,
                                                           valueExpression: g => g.SortOrder + 1));

    var group = new InfrastructureGroup { Name = normalizedName, MonitorId = parsedMonitorId, SortOrder = position };

    this.context.InfrastructureGroups.Add (group);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new
                           {
                             group.Id, group.Name, group.MonitorId, group.SortOrder,
                           });
  }

  /// <summary>
  ///   Reorders infrastructure groups according to the provided ID sequence.
  /// </summary>
  /// <param name="orderedIds">The desired ordered set of group IDs.</param>
  /// <returns>A JSON payload confirming success, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostReorderInfrastructureGroupsAsync (string[]? orderedIds)
  {
    if (!OrderedIdParser.TryParse (values: orderedIds, ids: out List<int>? ids, error: out string? error))
      return this.BadRequestProblem (error);

    List<InfrastructureGroup> groups = await this.context.InfrastructureGroups
                                                 .Where (g => ids.Contains (g.Id))
                                                 .ToListAsync ();

    if (groups.Count != ids.Count)
      return this.BadRequestProblem ("One or more infrastructure groups do not exist.");

    for (var i = 0; i < ids.Count; i++)
    {
      InfrastructureGroup group = groups.First (g => g.Id == ids[i]);
      group.SortOrder = i;
    }

    await this.context.SaveChangesAsync ();

    return new JsonResult (new { Success = true });
  }

  /// <summary>
  ///   Reads and checks an uploaded dashboard file.
  /// </summary>
  /// <param name="file">The upload.</param>
  /// <returns>The configuration, or a 400 problem response listing what's wrong with the file (one problem per line).</returns>
  private async Task<(DashboardBackup? Backup, IActionResult? Problem)> ReadBackupAsync (IFormFile? file)
  {
    if ((file == null) || (file.Length == 0))
      return (null, this.BadRequestProblem ("Choose a dashboard file to import."));

    if (file.Length > SettingsModel.MaxBackupBytes)
      return (null, this.BadRequestProblem ("The file is too large to be a dashboard export."));

    using var reader = new StreamReader (stream: file.OpenReadStream (), encoding: Encoding.UTF8);
    string    json   = await reader.ReadToEndAsync (this.HttpContext.RequestAborted);

    return DashboardBackupService.TryRead (json: json,
                                           kinds: this.panelKinds,
                                           integrations: this.integrations,
                                           backup: out DashboardBackup? backup,
                                           errors: out List<string> errors)
             ? (backup, null)
             : (null, this.BadRequestProblem (string.Join (separator: '\n', values: errors)));
  }

  /// <summary>
  ///   Determines whether a posted value is missing or blank.
  /// </summary>
  /// <param name="value">The value.</param>
  /// <returns><c>true</c> when it is.</returns>
  private static bool Blank (string? value) => string.IsNullOrWhiteSpace (value);

  /// <summary>
  ///   Checks posted Uptime Kuma settings against the integration's schema and rules.
  /// </summary>
  /// <param name="serverUrl">The server's base URL.</param>
  /// <param name="statusPageSlug">The status page slug.</param>
  /// <param name="defaultDuration">The default uptime badge duration, in hours.</param>
  /// <param name="json">The settings as the connection stores them, when they're valid.</param>
  /// <param name="options">The settings, when they're valid.</param>
  /// <param name="problem">A 400 problem response saying what's wrong, when they aren't.</param>
  /// <returns><c>true</c> when the settings are valid.</returns>
  private bool TryReadUptimeKuma (string?                                  serverUrl,
                                  string?                                  statusPageSlug,
                                  string?                                  defaultDuration,
                                  [NotNullWhen (true)] out  string?        json,
                                  [NotNullWhen (true)] out  OptionValues?  options,
                                  [NotNullWhen (false)] out IActionResult? problem)
  {
    IIntegration integration = this.KumaIntegration;
    var posted = new Dictionary<string, string?>
                 {
                   [UptimeKumaIntegration.ServerUrlOption]       = serverUrl,
                   [UptimeKumaIntegration.StatusPageSlugOption]  = statusPageSlug,
                   [UptimeKumaIntegration.DefaultDurationOption] = defaultDuration,
                 };

    options = null;
    problem = null;

    if (!OptionForm.TryParse (schema: integration.ConnectionOptions, posted: posted, json: out json, error: out string? error))
    {
      problem = this.BadRequestProblem (error);

      return false;
    }

    options = new OptionValues (schema: integration.ConnectionOptions, json: json);

    if (integration.Validate (options) is not { } invalid)
      return true;

    problem = this.BadRequestProblem (invalid);
    json    = null;
    options = null;

    return false;
  }

  /// <summary>
  ///   Builds the ordered infrastructure group query used by the settings UI.
  /// </summary>
  /// <returns>An <see cref="IQueryable{T}" /> that orders groups by sort order and name.</returns>
  private IQueryable<InfrastructureGroup> GetOrderedInfrastructureGroupsQuery ()
    => this.context.InfrastructureGroups
           .OrderBy (g => g.SortOrder)
           .ThenBy (g => g.Name);
}

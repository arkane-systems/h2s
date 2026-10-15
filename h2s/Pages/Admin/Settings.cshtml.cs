using System.Text;
using System.Text.Json;
using h2s.Data;
using h2s.Models;
using h2s.Panels;
using h2s.Services;
using h2s.Validation;
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

  /// <summary>
  ///   The message for an Uptime Kuma server URL that isn't a base URL.
  /// </summary>
  private const string InvalidKumaUrl =
    "Uptime Kuma server URL must be a valid HTTP or HTTPS base URL with no path, query, or fragment.";

  private readonly DashboardBackupService   backupService;
  private readonly DashboardContext         context;
  private readonly PanelCatalog             panelKinds;
  private readonly DashboardSettingsService settingsService;
  private readonly UptimeKumaClient         uptimeKumaClient;
  private readonly UrlProbe                 urlProbe;

  /// <summary>
  ///   Initializes a new instance of the <see cref="SettingsModel" /> class.
  /// </summary>
  /// <param name="settingsService">The service used to load and save dashboard settings.</param>
  /// <param name="context">The database context used to manage infrastructure groups.</param>
  /// <param name="urlProbe">The probe used to check that the Uptime Kuma server is reachable.</param>
  /// <param name="backupService">The service used to export and import the whole configuration.</param>
  /// <param name="uptimeKumaClient">The client used to check the Uptime Kuma status page.</param>
  /// <param name="panelKinds">The panel kinds an imported file's panels may use.</param>
  public SettingsModel (DashboardSettingsService settingsService,
                        DashboardContext         context,
                        UrlProbe                 urlProbe,
                        DashboardBackupService   backupService,
                        UptimeKumaClient         uptimeKumaClient,
                        PanelCatalog             panelKinds)
  {
    this.panelKinds       = panelKinds;
    this.settingsService  = settingsService;
    this.context          = context;
    this.urlProbe         = urlProbe;
    this.backupService    = backupService;
    this.uptimeKumaClient = uptimeKumaClient;
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
  ///   Gets a value indicating whether Uptime Kuma is configured, which shows the infrastructure groups.
  /// </summary>
  public bool IsUptimeKumaConfigured => !string.IsNullOrWhiteSpace (this.Settings.UptimeKumaServerUrl);

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
                                       },
                           });
  }

  /// <summary>
  ///   Replaces the whole configuration (settings, categories, links, infrastructure groups, panels and search engines) with an uploaded
  ///   dashboard file's.
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
    this.Settings = await this.settingsService.GetSettingsAsync ();
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
  ///   Saves the Uptime Kuma part of the Integrations section.
  /// </summary>
  /// <param name="serverUrl">The server's base URL; blank turns the integration off.</param>
  /// <param name="statusPageSlug">The status page slug; blank for none.</param>
  /// <param name="defaultDuration">The default uptime badge duration, in whole hours of at least 1.</param>
  /// <returns>A JSON payload with the saved values, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostSaveUptimeKumaAsync (string? serverUrl, string? statusPageSlug, string? defaultDuration)
  {
    string? url = SettingsModel.Blank (serverUrl) ? null : serverUrl!.Trim ();

    if (!new HttpBaseUrlAttribute ().IsValid (url))
      return this.BadRequestProblem (SettingsModel.InvalidKumaUrl);

    if (!StatusPageSlug.IsValid (statusPageSlug))
      return this.BadRequestProblem (StatusPageSlug.InvalidMessage);

    if (!int.TryParse (s: defaultDuration?.Trim (), result: out int duration) || (duration < 1))
      return this.BadRequestProblem ("Default uptime duration must be a whole number of hours, at least 1.");

    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();
    settings.UptimeKumaServerUrl       = url;
    settings.UptimeKumaStatusPageSlug  = SettingsModel.Blank (statusPageSlug) ? null : statusPageSlug!.Trim ();
    settings.UptimeKumaDefaultDuration = duration;
    await this.settingsService.SaveSettingsAsync (settings);

    return new JsonResult (new
                           {
                             ServerUrl       = settings.UptimeKumaServerUrl,
                             StatusPageSlug  = settings.UptimeKumaStatusPageSlug,
                             DefaultDuration = settings.UptimeKumaDefaultDuration,
                             IsConfigured    = settings.UptimeKumaServerUrl != null,
                           });
  }

  /// <summary>
  ///   Tests Uptime Kuma settings without saving them: whether the server answers, whether the h2s server trusts its
  ///   certificate, and whether the status page exists and lists monitors.
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

    string url = serverUrl!.Trim ();

    if (!new HttpBaseUrlAttribute ().IsValid (url))
      return this.BadRequestProblem (SettingsModel.InvalidKumaUrl);

    if (!StatusPageSlug.IsValid (statusPageSlug))
      return this.BadRequestProblem (StatusPageSlug.InvalidMessage);

    CancellationToken cancellationToken = this.HttpContext.RequestAborted;

    switch (await this.urlProbe.CheckAsync (url: url, cancellationToken: cancellationToken))
    {
      case UrlReachability.Unreachable:
        return SettingsModel.TestResult (level: "danger", message: $"Couldn't reach Uptime Kuma at {url}.");

      case UrlReachability.UntrustedCertificate:
        return SettingsModel.TestResult (level: "warning",
                                         message:
                                         "Uptime Kuma answers, but the h2s server doesn't trust its certificate. Badges still load in " +
                                         "browsers that trust it, but the monitor picker can't list monitors until the CA is mounted " +
                                         "(see the README's section on trusting a homelab certificate authority).");
    }

    if (SettingsModel.Blank (statusPageSlug))
    {
      return SettingsModel.TestResult (level: "warning",
                                       message:
                                       "Connected to Uptime Kuma. Add a status page slug to link the status bar and pick " +
                                       "monitors from the page.");
    }

    KumaMonitorList monitors = await this.uptimeKumaClient.GetStatusPageMonitorsAsync (serverUrl: url,
                                                                                       slug: statusPageSlug!,
                                                                                       cancellationToken: cancellationToken);

    if (monitors.Message != null)
      return SettingsModel.TestResult (level: "danger", message: $"Connected to Uptime Kuma, but: {monitors.Message}");

    int count = monitors.Groups.Sum (g => g.Monitors.Count);

    return SettingsModel.TestResult (level: count > 0 ? "success" : "warning",
                                     message: count > 0
                                                ? $"Connected. The status page lists {count} monitor{(count == 1 ? "" : "s")} in " +
                                                  $"{monitors.Groups.Count} group{(monitors.Groups.Count    == 1 ? "" : "s")}."
                                                : "Connected, but the status page lists no monitors, so badges will show N/A.");
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
  ///   Builds a Test connection result.
  /// </summary>
  /// <param name="level">The Bootstrap alert level: <c>success</c>, <c>warning</c> or <c>danger</c>.</param>
  /// <param name="message">What the test found.</param>
  /// <returns>The JSON result.</returns>
  private static JsonResult TestResult (string level, string message) => new (new { Level = level, Message = message });

  /// <summary>
  ///   Builds the ordered infrastructure group query used by the settings UI.
  /// </summary>
  /// <returns>An <see cref="IQueryable{T}" /> that orders groups by sort order and name.</returns>
  private IQueryable<InfrastructureGroup> GetOrderedInfrastructureGroupsQuery ()
    => this.context.InfrastructureGroups
           .OrderBy (g => g.SortOrder)
           .ThenBy (g => g.Name);
}

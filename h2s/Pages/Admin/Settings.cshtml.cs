using System.Text;
using System.Text.Json;
using h2s.Data;
using h2s.Models;
using h2s.Services;
using h2s.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages.Admin;

/// <summary>
///   Page model for editing the singleton dashboard settings record.
/// </summary>
public class SettingsModel : PageModel
{
  // A dashboard file is a few kilobytes per hundred links, so this is generous.
  private const long MaxBackupBytes = 2 * 1024 * 1024;

  private readonly DashboardBackupService   backupService;
  private readonly DashboardContext         context;
  private readonly DashboardSettingsService settingsService;
  private readonly UrlProbe                 urlProbe;

  /// <summary>
  ///   Initializes a new instance of the <see cref="SettingsModel" /> class.
  /// </summary>
  /// <param name="settingsService">The service used to load and save dashboard settings.</param>
  /// <param name="context">The database context used to manage infrastructure groups.</param>
  /// <param name="urlProbe">The probe used to check that the Uptime Kuma server is reachable.</param>
  /// <param name="backupService">The service used to export and import the whole configuration.</param>
  public SettingsModel (DashboardSettingsService settingsService,
                        DashboardContext         context,
                        UrlProbe                 urlProbe,
                        DashboardBackupService   backupService)
  {
    this.settingsService = settingsService;
    this.context         = context;
    this.urlProbe        = urlProbe;
    this.backupService   = backupService;
  }

  /// <summary>
  ///   Gets or sets the settings values bound to the edit form.
  /// </summary>
  [BindProperty]
  public DashboardSettings Settings { get; set; } = default!;

  /// <summary>
  ///   Gets the infrastructure groups configured for the status bar.
  /// </summary>
  public List<InfrastructureGroup> InfrastructureGroups { get; private set; } = new ();

  /// <summary>
  ///   Gets or sets a warning shown when the configured Uptime Kuma URL is not reachable.
  /// </summary>
  [TempData]
  public string? UptimeKumaWarning { get; set; }

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
                                       },
                           });
  }

  /// <summary>
  ///   Replaces the whole configuration (settings, categories, links and infrastructure groups) with an uploaded
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
  }

  /// <summary>
  ///   Saves the posted dashboard settings and reloads the page.
  /// </summary>
  /// <returns>The current page when validation fails; otherwise a redirect back to the page.</returns>
  public async Task<IActionResult> OnPostAsync ()
  {
    if (!this.ModelState.IsValid)
    {
      this.InfrastructureGroups = await this.GetOrderedInfrastructureGroupsQuery ()
                                            .AsNoTracking ()
                                            .ToListAsync ();

      return this.Page ();
    }

    if (!string.IsNullOrWhiteSpace (this.Settings.UptimeKumaServerUrl))
    {
      UrlReachability reachability = await this.urlProbe.CheckAsync (url: this.Settings.UptimeKumaServerUrl,
                                                                     cancellationToken: this.HttpContext.RequestAborted);
      this.UptimeKumaWarning = reachability switch
                               {
                                 UrlReachability.Unreachable =>
                                   $"The Uptime Kuma server at {this.Settings.UptimeKumaServerUrl} could not be reached. Please verify the URL is correct and the server is accessible.",
                                 UrlReachability.UntrustedCertificate =>
                                   $"The Uptime Kuma server at {this.Settings.UptimeKumaServerUrl} is reachable, but it is using a certificate signed by an internal or private CA that this container does not trust. Uptime Kuma badges and the status bar will not work until and unless the internal root certificate is installed on the clients.",
                                 _ => null,
                               };
    }

    await this.settingsService.SaveSettingsAsync (this.Settings);

    return this.RedirectToPage ();
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
  /// <returns>A JSON payload describing the deleted group, or a 404 problem response when it does not exist.</returns>
  public async Task<IActionResult> OnPostDeleteInfrastructureGroupAsync (int id)
  {
    InfrastructureGroup? group = await this.context.InfrastructureGroups.FirstOrDefaultAsync (g => g.Id == id);

    if (group == null)
      return this.NotFoundProblem ("Infrastructure group not found.");

    this.context.InfrastructureGroups.Remove (group);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id });
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

    return DashboardBackupService.TryRead (json: json, backup: out DashboardBackup? backup, errors: out List<string> errors)
             ? (backup, null)
             : (null, this.BadRequestProblem (string.Join (separator: '\n', values: errors)));
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

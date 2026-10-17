using System.Text;
using System.Text.Json;
using h2s.Data;
using h2s.Integrations;
using h2s.Models;
using h2s.Panels;
using h2s.Services;
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
  /// <param name="context">The database context, for the backup preview's counts.</param>
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
    {
      return problem!;
    }

    return new JsonResult (new
                           {
                             Incoming = DashboardBackupService.Summarize (backup),
                             Current = new
                                       {
                                         Categories    = await this.context.Categories.CountAsync (),
                                         Links         = await this.context.Links.CountAsync (),
                                         Panels        = await this.context.Panels.CountAsync (),
                                         SearchEngines = await this.context.SearchEngines.CountAsync (),
                                         Connections   = await this.context.Connections.CountAsync (),
                                         Widgets       = await this.context.Widgets.CountAsync (),
                                       },
                           });
  }

  /// <summary>
  ///   Replaces the whole configuration (settings, categories, links, panels, search engines, connections and widgets)
  ///   with an uploaded dashboard file's.
  /// </summary>
  /// <param name="file">The dashboard file.</param>
  /// <returns>A JSON payload summarizing what was imported, or a 400 problem response listing what is wrong with the file.</returns>
  public async Task<IActionResult> OnPostImportAsync (IFormFile? file)
  {
    (DashboardBackup? backup, IActionResult? problem) = await this.ReadBackupAsync (file);

    if (backup == null)
    {
      return problem!;
    }

    await this.backupService.ImportAsync (backup: backup, cancellationToken: this.HttpContext.RequestAborted);

    return new JsonResult (DashboardBackupService.Summarize (backup));
  }

  /// <summary>
  ///   Loads the current dashboard settings for display.
  /// </summary>
  public async Task OnGetAsync ()
  {
    this.Settings         = await this.settingsService.GetSettingsAsync ();
    this.IntegrationCards = await this.DescribeIntegrationsAsync ();
    this.SearchEngines    = await this.GetOrderedSearchEnginesAsync ();
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
    {
      return this.BadRequestProblem ("Title is required.");
    }

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
    {
      return this.BadRequestProblem ("Color mode must be Auto, Light or Dark.");
    }

    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();
    settings.ColorMode = mode;
    await this.settingsService.SaveSettingsAsync (settings);

    return new JsonResult (new { ColorMode = mode.ToString () });
  }

  /// <summary>
  ///   Reads and checks an uploaded dashboard file.
  /// </summary>
  /// <param name="file">The upload.</param>
  /// <returns>The configuration, or a 400 problem response listing what's wrong with the file (one problem per line).</returns>
  private async Task<(DashboardBackup? Backup, IActionResult? Problem)> ReadBackupAsync (IFormFile? file)
  {
    if ((file == null) || (file.Length == 0))
    {
      return (null, this.BadRequestProblem ("Choose a dashboard file to import."));
    }

    if (file.Length > SettingsModel.MaxBackupBytes)
    {
      return (null, this.BadRequestProblem ("The file is too large to be a dashboard export."));
    }

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
}

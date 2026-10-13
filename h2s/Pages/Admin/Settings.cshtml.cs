using h2s.Data;
using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages.Admin;

/// <summary>
///   Page model for editing the singleton dashboard settings record.
/// </summary>
public class SettingsModel : PageModel
{
  private readonly DashboardContext         context;
  private readonly DashboardSettingsService settingsService;
  private readonly UrlProbe                 urlProbe;

  /// <summary>
  ///   Initializes a new instance of the <see cref="SettingsModel" /> class.
  /// </summary>
  /// <param name="settingsService">The service used to load and save dashboard settings.</param>
  /// <param name="context">The database context used to manage infrastructure groups.</param>
  /// <param name="urlProbe">The probe used to check that the Uptime Kuma server is reachable.</param>
  public SettingsModel (DashboardSettingsService settingsService, DashboardContext context, UrlProbe urlProbe)
  {
    this.settingsService = settingsService;
    this.context         = context;
    this.urlProbe        = urlProbe;
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
  /// <param name="monitorId">The monitor ID as a positive integer string.</param>
  /// <returns>A JSON payload describing the created group, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateInfrastructureGroupAsync (string? name, string? monitorId)
  {
    string normalizedName      = (name      ?? string.Empty).Trim ();
    string normalizedMonitorId = (monitorId ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
      return this.BadRequest ("Group name is required.");

    if (!SettingsModel.IsPositiveIntegerString (normalizedMonitorId))
      return this.BadRequest ("Monitor ID must be a positive integer.");

    int nextSortOrder = await this.context.InfrastructureGroups
                                  .Select (g => (int?)g.SortOrder)
                                  .MaxAsync () ??
                        -1;

    var group = new InfrastructureGroup { Name = normalizedName, MonitorId = normalizedMonitorId, SortOrder = nextSortOrder + 1 };

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
  /// <returns>A JSON payload describing the updated group, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostUpdateInfrastructureGroupAsync (int id, string? name, string? monitorId)
  {
    string normalizedName      = (name      ?? string.Empty).Trim ();
    string normalizedMonitorId = (monitorId ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
      return this.BadRequest ("Group name is required.");

    if (!SettingsModel.IsPositiveIntegerString (normalizedMonitorId))
      return this.BadRequest ("Monitor ID must be a positive integer.");

    InfrastructureGroup? group = await this.context.InfrastructureGroups.FirstOrDefaultAsync (g => g.Id == id);

    if (group == null)
      return this.NotFound ();

    group.Name      = normalizedName;
    group.MonitorId = normalizedMonitorId;
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
  /// <returns>A JSON payload describing the deleted group, or <see cref="NotFoundResult" /> when it does not exist.</returns>
  public async Task<IActionResult> OnPostDeleteInfrastructureGroupAsync (int id)
  {
    InfrastructureGroup? group = await this.context.InfrastructureGroups.FirstOrDefaultAsync (g => g.Id == id);

    if (group == null)
      return this.NotFound ();

    this.context.InfrastructureGroups.Remove (group);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id });
  }

  /// <summary>
  ///   Reorders infrastructure groups according to the provided ID sequence.
  /// </summary>
  /// <param name="orderedIds">The desired ordered set of group IDs.</param>
  /// <returns>A JSON payload confirming success, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostReorderInfrastructureGroupsAsync (string[]? orderedIds)
  {
    if ((orderedIds == null) || (orderedIds.Length == 0))
      return this.BadRequest ("Ordered IDs are required.");

    var ids = new List<int> (orderedIds.Length);

    foreach (string idValue in orderedIds)
    {
      if (!int.TryParse (s: idValue, result: out int parsedId) || (parsedId <= 0))
        return this.BadRequest ("Ordered IDs must contain valid positive integers.");

      ids.Add (parsedId);
    }

    List<InfrastructureGroup> groups = await this.context.InfrastructureGroups
                                                 .Where (g => ids.Contains (g.Id))
                                                 .ToListAsync ();

    if (groups.Count != ids.Count)
      return this.BadRequest ("One or more infrastructure groups do not exist.");

    for (var i = 0; i < ids.Count; i++)
    {
      InfrastructureGroup group = groups.First (g => g.Id == ids[i]);
      group.SortOrder = i;
    }

    await this.context.SaveChangesAsync ();

    return new JsonResult (new { Success = true });
  }

  /// <summary>
  ///   Builds the ordered infrastructure group query used by the settings UI.
  /// </summary>
  /// <returns>An <see cref="IQueryable{T}" /> that orders groups by sort order and name.</returns>
  private IQueryable<InfrastructureGroup> GetOrderedInfrastructureGroupsQuery ()
    => this.context.InfrastructureGroups
           .OrderBy (g => g.SortOrder)
           .ThenBy (g => g.Name);

  /// <summary>
  ///   Determines whether the supplied string value is a positive integer.
  /// </summary>
  /// <param name="value">The value to validate.</param>
  /// <returns><c>true</c> when the value is a positive integer string; otherwise, <c>false</c>.</returns>
  private static bool IsPositiveIntegerString (string value)
    => int.TryParse (s: value, result: out int parsedValue) && (parsedValue > 0);
}

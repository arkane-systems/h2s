using h2s.Data;
using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages.Admin;

/// <summary>
/// Page model for editing the singleton dashboard settings record.
/// </summary>
public class SettingsModel : PageModel
{
  private readonly DashboardSettingsService _settingsService;
  private readonly DashboardContext _context;
  private readonly IHttpClientFactory _httpClientFactory;

  /// <summary>
  /// Initializes a new instance of the <see cref="SettingsModel"/> class.
  /// </summary>
  /// <param name="settingsService">The service used to load and save dashboard settings.</param>
  /// <param name="context">The database context used to manage infrastructure groups.</param>
  /// <param name="httpClientFactory">The HTTP client factory used for URL reachability checks.</param>
  public SettingsModel (DashboardSettingsService settingsService, DashboardContext context, IHttpClientFactory httpClientFactory)
  {
    _settingsService = settingsService;
    _context = context;
    _httpClientFactory = httpClientFactory;
  }

  /// <summary>
  /// Gets or sets the settings values bound to the edit form.
  /// </summary>
  [BindProperty]
  public DashboardSettings Settings { get; set; } = default!;

  /// <summary>
  /// Gets the infrastructure groups configured for the status bar.
  /// </summary>
  public List<InfrastructureGroup> InfrastructureGroups { get; private set; } = new ();

  /// <summary>
  /// Gets or sets a warning shown when the configured Uptime Kuma URL is not reachable.
  /// </summary>
  [TempData]
  public string? UptimeKumaWarning { get; set; }

  /// <summary>
  /// Loads the current dashboard settings for display.
  /// </summary>
  public async Task OnGetAsync ()
  {
    Settings = await _settingsService.GetSettingsAsync ();
    InfrastructureGroups = await GetOrderedInfrastructureGroupsQuery ()
      .AsNoTracking ()
      .ToListAsync ();
  }

  /// <summary>
  /// Saves the posted dashboard settings and reloads the page.
  /// </summary>
  /// <returns>The current page when validation fails; otherwise a redirect back to the page.</returns>
  public async Task<IActionResult> OnPostAsync ()
  {
    if (!ModelState.IsValid)
    {
      InfrastructureGroups = await GetOrderedInfrastructureGroupsQuery ()
        .AsNoTracking ()
        .ToListAsync ();
      return Page ();
    }

    if (!string.IsNullOrWhiteSpace (Settings.UptimeKumaServerUrl))
    {
      var isReachable = await IsUrlReachableAsync (Settings.UptimeKumaServerUrl);
      if (!isReachable)
      {
        UptimeKumaWarning = $"The Uptime Kuma server at {Settings.UptimeKumaServerUrl} could not be reached. Please verify the URL is correct and the server is accessible.";
      }
    }

    await _settingsService.SaveSettingsAsync (Settings);
    return RedirectToPage ();
  }

  /// <summary>
  /// Returns the ordered infrastructure group list used by the settings client script.
  /// </summary>
  /// <returns>A JSON payload describing all infrastructure groups.</returns>
  public async Task<IActionResult> OnGetInfrastructureGroupsAsync ()
  {
    var groups = await GetOrderedInfrastructureGroupsQuery ()
      .AsNoTracking ()
      .Select (g => new
      {
        g.Id,
        g.Name,
        g.MonitorId,
        g.SortOrder
      })
      .ToListAsync ();

    return new JsonResult (groups);
  }

  /// <summary>
  /// Creates a new infrastructure group from posted values.
  /// </summary>
  /// <param name="name">The group display name.</param>
  /// <param name="monitorId">The monitor ID as a positive integer string.</param>
  /// <returns>A JSON payload describing the created group, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateInfrastructureGroupAsync (string? name, string? monitorId)
  {
    var normalizedName = (name ?? string.Empty).Trim ();
    var normalizedMonitorId = (monitorId ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
    {
      return BadRequest ("Group name is required.");
    }

    if (!IsPositiveIntegerString (normalizedMonitorId))
    {
      return BadRequest ("Monitor ID must be a positive integer.");
    }

    var nextSortOrder = await _context.InfrastructureGroups
      .Select (g => (int?)g.SortOrder)
      .MaxAsync () ?? -1;

    var group = new InfrastructureGroup
    {
      Name = normalizedName,
      MonitorId = normalizedMonitorId,
      SortOrder = nextSortOrder + 1
    };

    _context.InfrastructureGroups.Add (group);
    await _context.SaveChangesAsync ();

    return new JsonResult (new
    {
      group.Id,
      group.Name,
      group.MonitorId,
      group.SortOrder
    });
  }

  /// <summary>
  /// Updates an existing infrastructure group.
  /// </summary>
  /// <param name="id">The group identifier.</param>
  /// <param name="name">The updated display name.</param>
  /// <param name="monitorId">The updated monitor ID.</param>
  /// <returns>A JSON payload describing the updated group, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostUpdateInfrastructureGroupAsync (int id, string? name, string? monitorId)
  {
    var normalizedName = (name ?? string.Empty).Trim ();
    var normalizedMonitorId = (monitorId ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
    {
      return BadRequest ("Group name is required.");
    }

    if (!IsPositiveIntegerString (normalizedMonitorId))
    {
      return BadRequest ("Monitor ID must be a positive integer.");
    }

    var group = await _context.InfrastructureGroups.FirstOrDefaultAsync (g => g.Id == id);
    if (group == null)
    {
      return NotFound ();
    }

    group.Name = normalizedName;
    group.MonitorId = normalizedMonitorId;
    await _context.SaveChangesAsync ();

    return new JsonResult (new
    {
      group.Id,
      group.Name,
      group.MonitorId,
      group.SortOrder
    });
  }

  /// <summary>
  /// Deletes an infrastructure group.
  /// </summary>
  /// <param name="id">The identifier of the group to remove.</param>
  /// <returns>A JSON payload describing the deleted group, or <see cref="NotFoundResult"/> when it does not exist.</returns>
  public async Task<IActionResult> OnPostDeleteInfrastructureGroupAsync (int id)
  {
    var group = await _context.InfrastructureGroups.FirstOrDefaultAsync (g => g.Id == id);
    if (group == null)
    {
      return NotFound ();
    }

    _context.InfrastructureGroups.Remove (group);
    await _context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id });
  }

  /// <summary>
  /// Reorders infrastructure groups according to the provided ID sequence.
  /// </summary>
  /// <param name="orderedIds">The desired ordered set of group IDs.</param>
  /// <returns>A JSON payload confirming success, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostReorderInfrastructureGroupsAsync (string[]? orderedIds)
  {
    if (orderedIds == null || orderedIds.Length == 0)
    {
      return BadRequest ("Ordered IDs are required.");
    }

    var ids = new List<int> (orderedIds.Length);
    foreach (var idValue in orderedIds)
    {
      if (!int.TryParse (idValue, out var parsedId) || parsedId <= 0)
      {
        return BadRequest ("Ordered IDs must contain valid positive integers.");
      }
      ids.Add (parsedId);
    }

    var groups = await _context.InfrastructureGroups
      .Where (g => ids.Contains (g.Id))
      .ToListAsync ();

    if (groups.Count != ids.Count)
    {
      return BadRequest ("One or more infrastructure groups do not exist.");
    }

    for (var i = 0; i < ids.Count; i++)
    {
      var group = groups.First (g => g.Id == ids[i]);
      group.SortOrder = i;
    }

    await _context.SaveChangesAsync ();
    return new JsonResult (new { Success = true });
  }

  /// <summary>
  /// Builds the ordered infrastructure group query used by the settings UI.
  /// </summary>
  /// <returns>An <see cref="IQueryable{T}"/> that orders groups by sort order and name.</returns>
  private IQueryable<InfrastructureGroup> GetOrderedInfrastructureGroupsQuery () => _context.InfrastructureGroups
    .OrderBy (g => g.SortOrder)
    .ThenBy (g => g.Name);

  /// <summary>
  /// Determines whether the supplied string value is a positive integer.
  /// </summary>
  /// <param name="value">The value to validate.</param>
  /// <returns><c>true</c> when the value is a positive integer string; otherwise, <c>false</c>.</returns>
  private static bool IsPositiveIntegerString (string value)
  {
    return int.TryParse (value, out var parsedValue) && parsedValue > 0;
  }

  /// <summary>
  /// Checks whether a URL is reachable using an HTTP HEAD request.
  /// </summary>
  /// <param name="url">The URL to check.</param>
  /// <returns><c>true</c> when the URL responds with a success status code; otherwise, <c>false</c>.</returns>
  private async Task<bool> IsUrlReachableAsync (string url)
  {
    ArgumentNullException.ThrowIfNull (url);

    var client = _httpClientFactory.CreateClient ();
    client.Timeout = TimeSpan.FromSeconds (5);

    try
    {
      using var request = new HttpRequestMessage (HttpMethod.Head, url);
      using var response = await client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead);
      return response.IsSuccessStatusCode;
    }
    catch
    {
      return false;
    }
  }
}

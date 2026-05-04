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
      var reachability = await CheckUrlReachabilityAsync (Settings.UptimeKumaServerUrl);
      UptimeKumaWarning = reachability switch
      {
        UrlReachability.Unreachable => $"The Uptime Kuma server at {Settings.UptimeKumaServerUrl} could not be reached. Please verify the URL is correct and the server is accessible.",
        UrlReachability.UntrustedCertificate => $"The Uptime Kuma server at {Settings.UptimeKumaServerUrl} is reachable, but it is using a certificate signed by an internal or private CA that this container does not trust. Uptime Kuma badges and the status bar will not work until and unless the internal root certificate is installed on the clients.",
        _ => null
      };
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
  /// Checks whether a URL is reachable using an HTTP HEAD request, falling back to GET if HEAD is not supported.
  /// Distinguishes between a genuine network failure and a certificate chain error caused by an untrusted internal CA.
  /// </summary>
  /// <param name="url">The URL to check.</param>
  /// <returns>A <see cref="UrlReachability"/> value describing the outcome.</returns>
  private async Task<UrlReachability> CheckUrlReachabilityAsync (string url)
  {
    ArgumentNullException.ThrowIfNull (url);

    var client = _httpClientFactory.CreateClient ();
    client.Timeout = TimeSpan.FromSeconds (5);

    try
    {
      using var headRequest = new HttpRequestMessage (HttpMethod.Head, url);
      using var headResponse = await client.SendAsync (headRequest, HttpCompletionOption.ResponseHeadersRead);

      if (headResponse.IsSuccessStatusCode)
      {
        return UrlReachability.Reachable;
      }

      // Fall back to GET when HEAD is not allowed
      if (headResponse.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
      {
        using var getRequest = new HttpRequestMessage (HttpMethod.Get, url);
        using var getResponse = await client.SendAsync (getRequest, HttpCompletionOption.ResponseHeadersRead);
        return getResponse.IsSuccessStatusCode ? UrlReachability.Reachable : UrlReachability.Unreachable;
      }

      return UrlReachability.Unreachable;
    }
    catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
    {
      // SSL/TLS handshake failed; probe again without certificate validation to distinguish
      // an untrusted internal CA from a genuine network failure.
      return await ProbeWithoutCertificateValidationAsync (url);
    }
    catch
    {
      return UrlReachability.Unreachable;
    }
  }

  /// <summary>
  /// Repeats the reachability probe using a handler that accepts any certificate, used to confirm the server
  /// is actually up when the normal probe fails due to an untrusted certificate chain.
  /// </summary>
  /// <param name="url">The URL to check.</param>
  /// <returns>
  /// <see cref="UrlReachability.UntrustedCertificate"/> when the server responds successfully despite the
  /// certificate error; <see cref="UrlReachability.Unreachable"/> otherwise.
  /// </returns>
  private static async Task<UrlReachability> ProbeWithoutCertificateValidationAsync (string url)
  {
    try
    {
      using var handler = new HttpClientHandler
      {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
      };
      using var client = new HttpClient (handler) { Timeout = TimeSpan.FromSeconds (5) };

      using var request = new HttpRequestMessage (HttpMethod.Head, url);
      using var response = await client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead);

      return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed
        ? UrlReachability.UntrustedCertificate
        : UrlReachability.Unreachable;
    }
    catch
    {
      return UrlReachability.Unreachable;
    }
  }

  /// <summary>Describes the outcome of a URL reachability probe.</summary>
  private enum UrlReachability
  {
    /// <summary>The server responded with a success status code.</summary>
    Reachable,
    /// <summary>The server could not be contacted or returned an error.</summary>
    Unreachable,
    /// <summary>The server was reachable but its certificate chain could not be verified due to an untrusted internal CA.</summary>
    UntrustedCertificate
  }
}

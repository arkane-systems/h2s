using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace h2s.Pages.Admin;

/// <summary>
/// Page model for editing the singleton dashboard settings record.
/// </summary>
public class SettingsModel : PageModel
{
  private readonly DashboardSettingsService _settingsService;

  /// <summary>
  /// Initializes a new instance of the <see cref="SettingsModel"/> class.
  /// </summary>
  /// <param name="settingsService">The service used to load and save dashboard settings.</param>
  public SettingsModel (DashboardSettingsService settingsService)
  {
    _settingsService = settingsService;
  }

  /// <summary>
  /// Gets or sets the settings values bound to the edit form.
  /// </summary>
  [BindProperty]
  public DashboardSettings Settings { get; set; } = default!;

  /// <summary>
  /// Loads the current dashboard settings for display.
  /// </summary>
  public async Task OnGetAsync ()
  {
    Settings = await _settingsService.GetSettingsAsync ();
  }

  /// <summary>
  /// Saves the posted dashboard settings and reloads the page.
  /// </summary>
  /// <returns>The current page when validation fails; otherwise a redirect back to the page.</returns>
  public async Task<IActionResult> OnPostAsync ()
  {
    if (!ModelState.IsValid)
    {
      return Page ();
    }

    await _settingsService.SaveSettingsAsync (Settings);
    return RedirectToPage ();
  }
}

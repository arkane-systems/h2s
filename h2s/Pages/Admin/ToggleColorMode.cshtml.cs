using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace h2s.Pages.Admin;

/// <summary>
/// Page model that cycles the dashboard color mode and returns the new mode as JSON.
/// </summary>
public class ToggleColorModeModel : PageModel
{
  private readonly DashboardSettingsService _settingsService;

  /// <summary>
  /// Initializes a new instance of the <see cref="ToggleColorModeModel"/> class.
  /// </summary>
  /// <param name="settingsService">The service used to retrieve and persist dashboard settings.</param>
  public ToggleColorModeModel(DashboardSettingsService settingsService)
  {
    _settingsService = settingsService;
  }

  /// <summary>
  /// Advances the configured color mode to the next option in the Auto → Light → Dark cycle.
  /// </summary>
  /// <returns>A JSON payload containing the updated color mode name.</returns>
  public async Task<IActionResult> OnPostAsync()
  {
    var settings = await _settingsService.GetSettingsAsync();
    // Cycle: Auto(0) → Light(1) → Dark(2) → Auto(0)
    settings.ColorMode = (ColorMode)(((int)settings.ColorMode + 1) % 3);
    await _settingsService.SaveSettingsAsync(settings);
    return new JsonResult(new { colorMode = settings.ColorMode.ToString() });
  }
}

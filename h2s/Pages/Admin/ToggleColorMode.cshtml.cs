using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace h2s.Pages.Admin;

public class ToggleColorModeModel : PageModel
{
  private readonly DashboardSettingsService _settingsService;

  public ToggleColorModeModel(DashboardSettingsService settingsService)
  {
    _settingsService = settingsService;
  }

  public async Task<IActionResult> OnPostAsync()
  {
    var settings = await _settingsService.GetSettingsAsync();
    // Cycle: Auto(0) → Light(1) → Dark(2) → Auto(0)
    settings.ColorMode = (ColorMode)(((int)settings.ColorMode + 1) % 3);
    await _settingsService.SaveSettingsAsync(settings);
    return new JsonResult(new { colorMode = settings.ColorMode.ToString() });
  }
}

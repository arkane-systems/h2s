using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace h2s.Pages.Admin;

public class SettingsModel : PageModel
{
  private readonly DashboardSettingsService _settingsService;

  public SettingsModel(DashboardSettingsService settingsService)
  {
    _settingsService = settingsService;
  }

  [BindProperty]
  public DashboardSettings Settings { get; set; } = default!;

  public async Task OnGetAsync()
  {
    Settings = await _settingsService.GetSettingsAsync();
  }

  public async Task<IActionResult> OnPostAsync()
  {
    if (!ModelState.IsValid)
    {
      return Page();
    }

    await _settingsService.SaveSettingsAsync(Settings);
    return RedirectToPage();
  }
}

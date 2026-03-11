namespace h2s.Services;

using h2s.Data;
using h2s.Models;
using Microsoft.EntityFrameworkCore;

public class DashboardSettingsService
{
  private readonly DashboardContext _context;

  public DashboardSettingsService(DashboardContext context)
  {
    _context = context;
  }

  /// <summary>
  /// Gets the singleton dashboard settings record, or creates a default one if it doesn't exist.
  /// </summary>
  public async Task<DashboardSettings> GetSettingsAsync()
  {
    var settings = await _context.DashboardSettings.FirstOrDefaultAsync();

    if (settings == null)
    {
      settings = new DashboardSettings { Id = 1, Title = "Dashboard" };
      _context.DashboardSettings.Add(settings);
      await _context.SaveChangesAsync();
    }

    return settings;
  }

  /// <summary>
  /// Saves changes to the singleton dashboard settings record to the database.
  /// </summary>
  public async Task SaveSettingsAsync(DashboardSettings settings)
  {
    _context.DashboardSettings.Update(settings);
    await _context.SaveChangesAsync();
  }
}

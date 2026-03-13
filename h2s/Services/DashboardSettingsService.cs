namespace h2s.Services;

using h2s.Data;
using h2s.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Provides access to the singleton <see cref="DashboardSettings"/> record.
/// </summary>
public class DashboardSettingsService
{
  private readonly DashboardContext _context;

  /// <summary>
  /// Initializes a new instance of the <see cref="DashboardSettingsService"/> class.
  /// </summary>
  /// <param name="context">The database context used to load and save settings.</param>
  public DashboardSettingsService (DashboardContext context)
  {
    _context = context;
  }

  /// <summary>
  /// Gets the singleton dashboard settings record, or creates a default one if it does not exist.
  /// </summary>
  /// <returns>The existing or newly created dashboard settings record.</returns>
  public async Task<DashboardSettings> GetSettingsAsync ()
  {
    var settings = await _context.DashboardSettings.FirstOrDefaultAsync ();

    if (settings == null)
    {
      settings = new DashboardSettings { Id = 1, Title = "Dashboard", Motto = "", LocalDomains = "" };
      _context.DashboardSettings.Add (settings);
      await _context.SaveChangesAsync ();
    }

    return settings;
  }

  /// <summary>
  /// Saves changes to the singleton dashboard settings record.
  /// </summary>
  /// <param name="settings">The settings instance to persist.</param>
  public async Task SaveSettingsAsync (DashboardSettings settings)
  {
    _context.DashboardSettings.Update (settings);
    await _context.SaveChangesAsync ();
  }
}

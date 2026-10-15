using h2s.Data;
using h2s.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Services;

/// <summary>
///   Provides access to the singleton <see cref="DashboardSettings" /> record.
/// </summary>
/// <remarks>
///   Settings are read on every page (the layout renders the title and theme), so they are cached in memory and the
///   cache is invalidated on save. Writes that bypass this service, such as the seeder, show up once the cache entry
///   expires.
/// </remarks>
public class DashboardSettingsService
{
  private const string CacheKey = "dashboard-settings";

  private const int DefaultUptimeKumaDuration = 24;

  private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes (5);

  private readonly IMemoryCache cache;

  private readonly DashboardContext context;

  /// <summary>
  ///   Initializes a new instance of the <see cref="DashboardSettingsService" /> class.
  /// </summary>
  /// <param name="context">The database context used to load and save settings.</param>
  /// <param name="cache">The cache that holds the current settings between requests.</param>
  public DashboardSettingsService (DashboardContext context, IMemoryCache cache)
  {
    this.context = context;
    this.cache   = cache;
  }

  /// <summary>
  ///   Gets the singleton dashboard settings record, or creates a default one if it does not exist.
  /// </summary>
  /// <returns>
  ///   A copy of the current settings, which the caller may change and pass to <see cref="SaveSettingsAsync" />.
  /// </returns>
  public async Task<DashboardSettings> GetSettingsAsync ()
  {
    if (!this.cache.TryGetValue (key: DashboardSettingsService.CacheKey, value: out DashboardSettings? settings) ||
        (settings == null))
    {
      settings = await this.LoadSettingsAsync ();
      this.cache.Set (key: DashboardSettingsService.CacheKey,
                      value: settings,
                      absoluteExpirationRelativeToNow: DashboardSettingsService.CacheDuration);
    }

    return settings.Clone ();
  }

  /// <summary>
  ///   Saves changes to the singleton dashboard settings record.
  /// </summary>
  /// <param name="settings">The settings instance to persist.</param>
  public async Task SaveSettingsAsync (DashboardSettings settings)
  {
    // There is only ever one settings row, whatever identifier a posted form carries.
    settings.Id = 1;

    this.context.DashboardSettings.Update (settings);
    await this.context.SaveChangesAsync ();

    this.cache.Remove (DashboardSettingsService.CacheKey);
  }

  /// <summary>
  ///   Loads the settings record from the database, creating it with default values if it does not exist.
  /// </summary>
  /// <returns>The settings, untracked and with an invalid uptime duration replaced by the default.</returns>
  private async Task<DashboardSettings> LoadSettingsAsync ()
  {
    DashboardSettings? settings = await this.context.DashboardSettings
                                            .AsNoTracking ()
                                            .SingleOrDefaultAsync (s => s.Id == 1);

    if (settings == null)
    {
      settings = new DashboardSettings
                 {
                   Id                        = 1,
                   Title                     = "Dashboard",
                   Motto                     = "",
                   LocalDomains              = "",
                   UptimeKumaDefaultDuration = DashboardSettingsService.DefaultUptimeKumaDuration,
                 };
      this.context.DashboardSettings.Add (settings);
      await this.context.SaveChangesAsync ();

      // Stop tracking the new row, so a later save of a copy doesn't conflict with it.
      this.context.Entry (settings).State = EntityState.Detached;
    }

    // Settings validation keeps the stored duration positive; correct an invalid one in memory only.
    if (settings.UptimeKumaDefaultDuration <= 0)
      settings.UptimeKumaDefaultDuration = DashboardSettingsService.DefaultUptimeKumaDuration;

    return settings;
  }
}

using h2s.Widgets.Abstractions;

namespace h2s.Integrations.UptimeKuma;

/// <summary>
///   The uptime windows Uptime Kuma reports (<c>monitor_uptime_ratio{window=…}</c>): one day, 30 days and 365 days.
/// </summary>
public static class UptimeKumaWindows
{
  /// <summary>
  ///   The one-day window, the default.
  /// </summary>
  public const string OneDay = "1d";

  /// <summary>
  ///   The 30-day window.
  /// </summary>
  public const string ThirtyDays = "30d";

  /// <summary>
  ///   The 365-day window.
  /// </summary>
  public const string OneYear = "365d";

  /// <summary>
  ///   The windows as a select option's choices, shortest first.
  /// </summary>
  public static readonly IReadOnlyList<OptionChoice> Choices =
  [
    new (Value: UptimeKumaWindows.OneDay, Label: "1 day"),
    new (Value: UptimeKumaWindows.ThirtyDays, Label: "30 days"),
    new (Value: UptimeKumaWindows.OneYear, Label: "365 days"),
  ];

  /// <summary>
  ///   Gets a window's length in hours, as Kuma's badge URLs take it.
  /// </summary>
  /// <param name="window">The window.</param>
  /// <returns>The hours; a day's for an unknown window.</returns>
  public static int ToHours (string? window)
    => window switch
       {
         UptimeKumaWindows.ThirtyDays => 30  * 24,
         UptimeKumaWindows.OneYear    => 365 * 24,
         _                            => 24,
       };

  /// <summary>
  ///   Gets the shortest window that covers a period in hours, for settings saved before windows replaced hours.
  /// </summary>
  /// <param name="hours">The period, in hours.</param>
  /// <returns>The window.</returns>
  public static string FromHours (int hours)
    => hours switch
       {
         <= 24      => UptimeKumaWindows.OneDay,
         <= 30 * 24 => UptimeKumaWindows.ThirtyDays,
         _          => UptimeKumaWindows.OneYear,
       };
}

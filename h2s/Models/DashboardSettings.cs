using System.ComponentModel.DataAnnotations;
using h2s.Validation;

namespace h2s.Models;

/// <summary>
/// Stores the singleton configuration used to render and personalize the dashboard.
/// </summary>
public class DashboardSettings
{
  /// <summary>
  /// Gets or sets the unique identifier for the settings record. This is always <c>1</c>.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  /// Gets or sets the main dashboard title displayed in the layout and page title.
  /// </summary>
  public string Title { get; set; } = "";

  /// <summary>
  /// Gets or sets the optional subtitle or motto displayed beneath the title.
  /// </summary>
  public string Motto { get; set; } = "";

  /// <summary>
  /// Gets or sets the list of domains treated as local when detecting external links.
  /// </summary>
  public string LocalDomains { get; set; } = "";

  /// <summary>
  /// Gets or sets the user's preferred color mode for the dashboard.
  /// </summary>
  public ColorMode ColorMode { get; set; } = ColorMode.Auto;

  /// <summary>
  /// Gets or sets the base URL of the Uptime Kuma server for monitoring badge integration.
  /// If configured, enables the infrastructure status bar and per-link monitoring badges.
  /// </summary>
  [Display (Name = "Uptime Kuma server URL")]
  [HttpBaseUrl (ErrorMessage = "Uptime Kuma server URL must be a valid HTTP or HTTPS base URL with no path, query, or fragment.")]
  public string? UptimeKumaServerUrl { get; set; }

  /// <summary>
  /// Gets or sets the slug for the Uptime Kuma status page to link from the status bar.
  /// </summary>
  [Display (Name = "Status page slug")]
  [RegularExpression ("^[A-Za-z0-9-]+$", ErrorMessage = "Status page slug may only contain letters, numbers, and hyphens.")]
  public string? UptimeKumaStatusPageSlug { get; set; }

  /// <summary>
  /// Gets or sets the default duration in hours for uptime badges.
  /// </summary>
  [Display (Name = "Default uptime duration (hours)")]
  [Range (1, int.MaxValue, ErrorMessage = "Default uptime duration (hours) must be at least 1.")]
  public int UptimeKumaDefaultDuration { get; set; } = 24;
}

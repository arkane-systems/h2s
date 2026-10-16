using System.ComponentModel.DataAnnotations;

namespace h2s.Models;

/// <summary>
///   Stores the singleton configuration used to render and personalize the dashboard.
/// </summary>
public class DashboardSettings
{
  /// <summary>
  ///   Gets or sets the unique identifier for the settings record. This is always <c>1</c>.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  ///   Gets or sets the main dashboard title displayed in the layout and page title.
  /// </summary>
  [Required]
  public string Title { get; set; } = "";

  /// <summary>
  ///   Gets or sets the optional subtitle or motto displayed beneath the title.
  /// </summary>
  /// <remarks>
  ///   Optional but non-nullable, so an empty form field must bind as an empty string rather than <c>null</c>.
  /// </remarks>
  [DisplayFormat (ConvertEmptyStringToNull = false)]
  public string Motto { get; set; } = "";

  /// <summary>
  ///   Gets or sets the list of domains treated as local when detecting external links.
  /// </summary>
  /// <remarks>Optional but non-nullable; see <see cref="Motto" />.</remarks>
  [DisplayFormat (ConvertEmptyStringToNull = false)]
  public string LocalDomains { get; set; } = "";

  /// <summary>
  ///   Gets or sets the user's preferred color mode for the dashboard.
  /// </summary>
  public ColorMode ColorMode { get; set; } = ColorMode.Auto;

  /// <summary>
  ///   Creates a copy of these settings.
  /// </summary>
  /// <returns>A new instance with the same values.</returns>
  public DashboardSettings Clone () => (DashboardSettings)this.MemberwiseClone ();
}

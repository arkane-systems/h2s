using System.ComponentModel.DataAnnotations;

namespace h2s.Models;

/// <summary>
///   A full-width dashboard card beside the link categories, such as the search box or the status bar. What it shows
///   comes from its kind (<see cref="h2s.Panels.IPanelKind" />) and that kind's options.
/// </summary>
public class Panel
{
  /// <summary>
  ///   The longest kind key that can be stored.
  /// </summary>
  public const int MaxKindLength = 64;

  /// <summary>
  ///   Gets or sets the unique identifier for the panel.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  ///   Gets or sets the key of the panel's kind, such as <c>search</c> or <c>status-bar</c>.
  /// </summary>
  [Required]
  [MaxLength (Panel.MaxKindLength)]
  public string Kind { get; set; } = "";

  /// <summary>
  ///   Gets or sets whether the panel sits above or below the categories.
  /// </summary>
  public PanelZone Zone { get; set; }

  /// <summary>
  ///   Gets or sets the position of the panel within its zone. Panels are ordered by this value in ascending order.
  /// </summary>
  public int SortOrder { get; set; }

  /// <summary>
  ///   Gets or sets the kind's option values, as a JSON object keyed by option name. Unknown keys are ignored and missing
  ///   ones take the option's default.
  /// </summary>
  public string OptionsJson { get; set; } = "{}";
}

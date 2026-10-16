using System.ComponentModel.DataAnnotations;

namespace h2s.Models;

/// <summary>
///   A widget on a link's tile, such as an Uptime Kuma monitor's status. What it shows comes from its kind
///   (<see cref="h2s.Widgets.Abstractions.IWidgetKind" />), which belongs to the integration of the connection it uses.
///   Deleting the link or the connection deletes the widget.
/// </summary>
public class Widget
{
  /// <summary>
  ///   The longest kind key that can be stored.
  /// </summary>
  public const int MaxKindLength = 64;

  /// <summary>
  ///   The shortest refresh interval a widget may choose, in seconds.
  /// </summary>
  public const int MinRefreshSeconds = 15;

  /// <summary>
  ///   The longest refresh interval a widget may choose, in seconds: a day.
  /// </summary>
  public const int MaxRefreshSeconds = 24 * 60 * 60;

  /// <summary>
  ///   Gets or sets the unique identifier for the widget.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  ///   Gets or sets the identifier of the link whose tile shows the widget.
  /// </summary>
  public int LinkId { get; set; }

  /// <summary>
  ///   Gets or sets the link whose tile shows the widget.
  /// </summary>
  public Link? Link { get; set; }

  /// <summary>
  ///   Gets or sets the identifier of the connection the widget reads its service through.
  /// </summary>
  public int ConnectionId { get; set; }

  /// <summary>
  ///   Gets or sets the connection the widget reads its service through.
  /// </summary>
  public Connection? Connection { get; set; }

  /// <summary>
  ///   Gets or sets the key of the widget's kind within the connection's integration, such as <c>monitor</c>.
  /// </summary>
  [Required]
  [MaxLength (Widget.MaxKindLength)]
  public string Kind { get; set; } = "";

  /// <summary>
  ///   Gets or sets the kind's option values, as a JSON object keyed by option name. Unknown keys are ignored and missing
  ///   ones take the option's default.
  /// </summary>
  public string OptionsJson { get; set; } = "{}";

  /// <summary>
  ///   Gets or sets how often the widget is read again, in seconds, or <c>null</c> for its kind's default.
  /// </summary>
  public int? RefreshSeconds { get; set; }

  /// <summary>
  ///   Gets or sets the widget's position among its link's widgets. Status widgets show in this order.
  /// </summary>
  public int SortOrder { get; set; }
}

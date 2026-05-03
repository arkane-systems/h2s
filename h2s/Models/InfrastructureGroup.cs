using System.ComponentModel.DataAnnotations;
using h2s.Validation;

namespace h2s.Models;

/// <summary>
/// Represents a group of infrastructure monitors displayed in the Uptime Kuma status bar.
/// </summary>
public class InfrastructureGroup
{
  /// <summary>
  /// Gets or sets the unique identifier for the infrastructure group.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  /// Gets or sets the display name for the infrastructure group shown above the badge pair.
  /// </summary>
  [Required]
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets the Uptime Kuma monitor ID for this infrastructure group.
  /// Must be a positive integer.
  /// </summary>
  [Required]
  [PositiveIntegerString]
  public string MonitorId { get; set; } = "";

  /// <summary>
  /// Gets or sets the sort order for displaying infrastructure groups in the status bar.
  /// Groups are ordered by this value in ascending order.
  /// </summary>
  public int SortOrder { get; set; }
}

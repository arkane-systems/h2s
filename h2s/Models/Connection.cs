using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace h2s.Models;

/// <summary>
///   A connection to another service, such as an Uptime Kuma server: one integration's settings, which the widgets and
///   panels that use the service share instead of repeating them. What the settings are comes from the integration
///   (<see cref="h2s.Widgets.Abstractions.IIntegration" />).
/// </summary>
[Index (propertyName: nameof (Connection.IntegrationKey), nameof (Connection.Name), IsUnique = true)]
public class Connection
{
  /// <summary>
  ///   The longest integration key that can be stored.
  /// </summary>
  public const int MaxIntegrationKeyLength = 64;

  /// <summary>
  ///   The longest name that can be stored.
  /// </summary>
  public const int MaxNameLength = 100;

  /// <summary>
  ///   Gets or sets the unique identifier for the connection.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  ///   Gets or sets the key of the connection's integration, such as <c>uptime-kuma</c>.
  /// </summary>
  [Required]
  [MaxLength (Connection.MaxIntegrationKeyLength)]
  public string IntegrationKey { get; set; } = "";

  /// <summary>
  ///   Gets or sets the connection's name, unique within its integration, such as "Home Assistant (upstairs)".
  /// </summary>
  [Required]
  [MaxLength (Connection.MaxNameLength)]
  public string Name { get; set; } = "";

  /// <summary>
  ///   Gets or sets the integration's settings, except secrets, as a JSON object keyed by option name. Unknown keys are
  ///   ignored and missing ones take the option's default.
  /// </summary>
  public string OptionsJson { get; set; } = "{}";

  /// <summary>
  ///   Gets or sets the integration's secret settings, as a JSON object keyed by option name and encrypted with Data
  ///   Protection, or <c>null</c> when none are set. Secrets are never exported or sent to a browser.
  /// </summary>
  public string? SecretsJson { get; set; }

  /// <summary>
  ///   Creates a copy of this connection.
  /// </summary>
  /// <returns>A new instance with the same values.</returns>
  public Connection Clone () => (Connection)this.MemberwiseClone ();
}

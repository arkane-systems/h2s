using h2s.Widgets.Abstractions;

namespace h2s.Widgets;

/// <summary>
///   A widget kind with the integration that offers it.
/// </summary>
/// <param name="Integration">The integration.</param>
/// <param name="Kind">The kind.</param>
public sealed record WidgetKindEntry (IIntegration Integration, IWidgetKind Kind)
{
  /// <summary>
  ///   Gets the kind's reference, <c>integration/kind</c>, as the browser and backups name it.
  /// </summary>
  public string Reference => WidgetKindEntry.ReferenceOf (integrationKey: this.Integration.Key, kindKey: this.Kind.Key);

  /// <summary>
  ///   Gets the name the link dialog shows, such as "Uptime Kuma: Monitor status".
  /// </summary>
  public string DisplayName => $"{this.Integration.DisplayName}: {this.Kind.DisplayName}";

  /// <summary>
  ///   Builds a kind's reference.
  /// </summary>
  /// <param name="integrationKey">The integration's key.</param>
  /// <param name="kindKey">The kind's key.</param>
  /// <returns>The reference, <c>integration/kind</c>.</returns>
  public static string ReferenceOf (string integrationKey, string kindKey) => $"{integrationKey}/{kindKey}";
}

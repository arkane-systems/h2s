namespace h2s.Services;

/// <summary>
///   The monitors on an Uptime Kuma status page, or why they couldn't be read.
/// </summary>
/// <param name="Groups">The status page's monitor groups, in order; empty when they couldn't be read.</param>
/// <param name="Message">Why the monitors couldn't be read, or <c>null</c> when they were.</param>
public sealed record KumaMonitorList (IReadOnlyList<KumaMonitorGroup> Groups, string? Message)
{
  /// <summary>
  ///   Creates a list for monitors that couldn't be read.
  /// </summary>
  /// <param name="message">Why.</param>
  /// <returns>The list.</returns>
  public static KumaMonitorList Failed (string message) => new (Groups: [], Message: message);
}

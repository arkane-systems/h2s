using System.Globalization;

namespace h2s.Validation;

/// <summary>
///   Parses Uptime Kuma monitor IDs posted by the admin pages.
/// </summary>
public static class MonitorIdParser
{
  /// <summary>
  ///   Parses a required monitor ID.
  /// </summary>
  /// <param name="value">The posted value; surrounding whitespace is ignored.</param>
  /// <param name="monitorId">The parsed monitor ID, when the value is valid.</param>
  /// <returns><c>true</c> when the value is a positive integer written in plain digits; otherwise, <c>false</c>.</returns>
  public static bool TryParse (string? value, out int monitorId)
    => int.TryParse (s: value?.Trim (), style: NumberStyles.None, provider: CultureInfo.InvariantCulture, result: out monitorId) &&
       (monitorId > 0);

  /// <summary>
  ///   Parses an optional monitor ID, where a blank value means "no monitor".
  /// </summary>
  /// <param name="value">The posted value; surrounding whitespace is ignored.</param>
  /// <param name="monitorId">The parsed monitor ID, or <c>null</c> for a blank value.</param>
  /// <returns><c>true</c> when the value is blank or a valid monitor ID; otherwise, <c>false</c>.</returns>
  public static bool TryParseOptional (string? value, out int? monitorId)
  {
    monitorId = null;

    if (string.IsNullOrWhiteSpace (value))
      return true;

    if (!MonitorIdParser.TryParse (value: value, monitorId: out int parsed))
      return false;

    monitorId = parsed;

    return true;
  }
}

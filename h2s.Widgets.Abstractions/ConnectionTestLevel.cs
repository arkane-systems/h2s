namespace h2s.Widgets.Abstractions;

/// <summary>
///   How a connection test went.
/// </summary>
public enum ConnectionTestLevel
{
  /// <summary>The connection works.</summary>
  Success = 0,

  /// <summary>The connection works in part, or works but something about it needs attention.</summary>
  Warning = 1,

  /// <summary>The connection doesn't work.</summary>
  Danger = 2,
}

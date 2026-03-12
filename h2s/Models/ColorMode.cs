namespace h2s.Models;

/// <summary>
/// Controls whether the dashboard uses light mode, dark mode, or automatically detects from the system.
/// </summary>
public enum ColorMode
{
  /// <summary>Automatically detect from the system's preferred color scheme.</summary>
  Auto = 0,
  /// <summary>Always use light mode.</summary>
  Light = 1,
  /// <summary>Always use dark mode.</summary>
  Dark = 2
}

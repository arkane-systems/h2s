namespace h2s.Widgets.Abstractions;

/// <summary>
///   How a widget's subject is doing, or how one of its figures should be read. The dashboard colours it.
/// </summary>
public enum WidgetStatus
{
  /// <summary>Not known: the service couldn't be read, or doesn't say.</summary>
  Unknown = 0,

  /// <summary>Working as it should.</summary>
  Ok = 1,

  /// <summary>Working, but something needs attention.</summary>
  Warning = 2,

  /// <summary>Not working.</summary>
  Error = 3,

  /// <summary>Deliberately not running, such as during maintenance.</summary>
  Inactive = 4,
}

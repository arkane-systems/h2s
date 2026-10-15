namespace h2s.Panels;

/// <summary>
///   The type of a panel option's value, which decides how it is stored, checked and edited.
/// </summary>
public enum PanelOptionType
{
  /// <summary>A line of text, stored as a JSON string.</summary>
  Text = 0,

  /// <summary>A whole number, stored as a JSON number. It may be blank (JSON <c>null</c>) when the option allows.</summary>
  Integer = 1,

  /// <summary>On or off, stored as a JSON <c>true</c> or <c>false</c>.</summary>
  Boolean = 2,
}

namespace h2s.Widgets.Abstractions;

/// <summary>
///   The type of an option's value, which decides how it is stored, checked and edited.
/// </summary>
public enum OptionType
{
  /// <summary>A line of text, stored as a JSON string.</summary>
  Text = 0,

  /// <summary>A whole number, stored as a JSON number. It may be blank (JSON <c>null</c>) when the option allows.</summary>
  Integer = 1,

  /// <summary>On or off, stored as a JSON <c>true</c> or <c>false</c>.</summary>
  Boolean = 2,

  /// <summary>An absolute HTTP or HTTPS URL without credentials, stored as a JSON string.</summary>
  Url = 3,

  /// <summary>
  ///   A value such as a password or API token. It's never stored with the other values, never sent to a browser, and
  ///   never exported: the core keeps it encrypted, and the edit form can only set, replace or clear it.
  /// </summary>
  Secret = 4,
}

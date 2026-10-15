namespace h2s.Validation;

/// <summary>
///   Checks Uptime Kuma status page slugs, which are made of letters, digits and hyphens.
/// </summary>
public static class StatusPageSlug
{
  /// <summary>
  ///   The message for a slug with other characters.
  /// </summary>
  public const string InvalidMessage = "Status page slug may only contain letters, numbers, and hyphens.";

  /// <summary>
  ///   Determines whether a slug is blank or made only of letters, digits and hyphens.
  /// </summary>
  /// <param name="slug">The slug; surrounding whitespace is ignored.</param>
  /// <returns><c>true</c> when it is.</returns>
  public static bool IsValid (string? slug)
    => string.IsNullOrWhiteSpace (slug) || slug.Trim ().All (c => char.IsAsciiLetterOrDigit (c) || (c == '-'));
}

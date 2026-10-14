namespace h2s.Services;

/// <summary>
///   What <see cref="PageMetadataReader" /> found on a page.
/// </summary>
/// <param name="Title">The page title, or <c>null</c> when it has none or the page could not be read.</param>
public sealed record PageMetadata (string? Title)
{
  /// <summary>
  ///   Metadata for a page that could not be read.
  /// </summary>
  public static readonly PageMetadata Empty = new (Title: null);
}

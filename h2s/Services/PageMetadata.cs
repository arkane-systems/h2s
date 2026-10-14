namespace h2s.Services;

/// <summary>
///   What <see cref="PageMetadataReader" /> found on a page and the pages it redirects to.
/// </summary>
/// <param name="Title">The page title, or <c>null</c> when it has none, it is generic, or it came from an error page.</param>
/// <param name="AppNames">The names the page declares for its app, best first, such as its web app manifest's name.</param>
public sealed record PageMetadata (string? Title, IReadOnlyList<string> AppNames)
{
  /// <summary>
  ///   Metadata for a page that could not be read.
  /// </summary>
  public static readonly PageMetadata Empty = new (Title: null, AppNames: []);

  /// <summary>
  ///   Gets a value indicating whether nothing useful was found.
  /// </summary>
  public bool IsEmpty => (this.Title == null) && (this.AppNames.Count == 0);

  /// <summary>
  ///   Gets the best label for a link to the page: its title, or else the app's name.
  /// </summary>
  public string? SuggestedLabel => this.Title ?? this.AppNames.FirstOrDefault ();
}

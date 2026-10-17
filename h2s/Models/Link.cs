using System.Text.RegularExpressions;

namespace h2s.Models;

/// <summary>
///   Represents a navigable item displayed within a dashboard category.
/// </summary>
public class Link
{
  private const string SelfhStIconsBaseUrl = "https://cdn.jsdelivr.net/gh/selfhst/icons";

  /// <summary>
  ///   Gets or sets the unique identifier for the link.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  ///   Gets or sets the identifier of the category that owns the link.
  /// </summary>
  public int CategoryId { get; set; }

  /// <summary>
  ///   Gets or sets the display label shown for the link.
  /// </summary>
  public string Label { get; set; } = "";

  /// <summary>
  ///   Gets or sets the optional descriptive text shown beneath the label.
  /// </summary>
  public string Description { get; set; } = "";

  /// <summary>
  ///   Gets or sets the normalized selfh.st icon name for the link.
  /// </summary>
  public string IconName { get; set; } = "";

  /// <summary>
  ///   Gets or sets an absolute image URL, such as the site's favicon, used as the link's icon when it has no selfh.st
  ///   icon name. Empty for none.
  /// </summary>
  public string IconUrl { get; set; } = "";

  /// <summary>
  ///   Gets or sets the destination URL for the link.
  /// </summary>
  public string Url { get; set; } = "";

  /// <summary>
  ///   Gets or sets a value indicating whether the link opens in a new browser tab rather than the dashboard's.
  /// </summary>
  public bool OpenInNewTab { get; set; }

  /// <summary>
  ///   Gets or sets the position of the link within its category. Links are ordered by this value in ascending order.
  /// </summary>
  public int SortOrder { get; set; }

  /// <summary>
  ///   Gets or sets the navigation property for the link's category.
  /// </summary>
  public Category? Category { get; set; }

  /// <summary>
  ///   Gets or sets the widgets on the link's tile, in their order.
  /// </summary>
  public List<Widget> Widgets { get; set; } = [];

  /// <summary>
  ///   Gets the URL of the link's icon: its selfh.st icon when it has an icon name, otherwise its <see cref="IconUrl" />.
  /// </summary>
  /// <param name="format">
  ///   The desired selfh.st image format. Supported values are <c>png</c>, <c>svg</c>, and <c>webp</c>.
  /// </param>
  /// <returns>The icon URL, or an empty string when the link has no icon.</returns>
  public string GetIconUrl (string format = "png")
    => string.IsNullOrWhiteSpace (this.IconName) ? this.IconUrl : Link.BuildIconUrl (iconName: this.IconName, format: format);

  /// <summary>
  ///   Builds a selfh.st CDN URL for a normalized icon name and format.
  /// </summary>
  /// <param name="iconName">The icon name to normalize and include in the URL.</param>
  /// <param name="format">The desired image format. Unsupported values fall back to <c>png</c>.</param>
  /// <returns>The icon URL, or an empty string when the icon name is blank.</returns>
  public static string BuildIconUrl (string iconName, string format = "png")
  {
    string normalizedIconName = Link.NormalizeIconName (iconName);

    if (string.IsNullOrWhiteSpace (normalizedIconName))
    {
      return "";
    }

    string normalizedFormat = Link.NormalizeFormat (format);

    return $"{Link.SelfhStIconsBaseUrl}/{normalizedFormat}/{normalizedIconName}.{normalizedFormat}";
  }

  /// <summary>
  ///   Normalizes a user-provided icon name into the slug format expected by the selfh.st CDN.
  /// </summary>
  /// <param name="iconName">The raw icon name entered by the user.</param>
  /// <returns>A lowercase hyphenated icon name, or an empty string when the input is blank.</returns>
  public static string NormalizeIconName (string iconName)
  {
    if (string.IsNullOrWhiteSpace (iconName))
    {
      return "";
    }

    string normalized = Regex.Replace (input: iconName.Trim ().ToLowerInvariant (), pattern: "[^a-z0-9]+", replacement: "-");

    return normalized.Trim ('-');
  }

  /// <summary>
  ///   Normalizes an icon format to one supported by the selfh.st CDN.
  /// </summary>
  /// <param name="format">The requested image format.</param>
  /// <returns>A supported format string.</returns>
  private static string NormalizeFormat (string format)
  {
    string normalized = format.Trim ().ToLowerInvariant ();

    return normalized is "svg" or "png" or "webp" ? normalized : "png";
  }
}

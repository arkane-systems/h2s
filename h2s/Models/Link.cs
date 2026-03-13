using System.Text.RegularExpressions;

namespace h2s.Models;

/// <summary>
/// Represents a navigable item displayed within a dashboard category.
/// </summary>
public class Link
{
  private const string SelfhStIconsBaseUrl = "https://cdn.jsdelivr.net/gh/selfhst/icons";

  /// <summary>
  /// Gets or sets the unique identifier for the link.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  /// Gets or sets the identifier of the category that owns the link.
  /// </summary>
  public int CategoryId { get; set; }

  /// <summary>
  /// Gets or sets the display label shown for the link.
  /// </summary>
  public string Label { get; set; } = "";

  /// <summary>
  /// Gets or sets the optional descriptive text shown beneath the label.
  /// </summary>
  public string Description { get; set; } = "";

  /// <summary>
  /// Gets or sets the normalized selfh.st icon name for the link.
  /// </summary>
  public string IconName { get; set; } = "";

  /// <summary>
  /// Gets or sets the destination URL for the link.
  /// </summary>
  public string Url { get; set; } = "";

  /// <summary>
  /// Gets or sets the navigation property for the link's category.
  /// </summary>
  public Category? Category { get; set; }

  /// <summary>
  /// Builds the icon URL for this link using the configured icon name.
  /// </summary>
  /// <param name="format">The desired image format. Supported values are <c>png</c>, <c>svg</c>, and <c>webp</c>.</param>
  /// <returns>The icon URL, or an empty string when no icon name is configured.</returns>
  public string GetIconUrl (string format = "png") => BuildIconUrl (this.IconName, format);

  /// <summary>
  /// Builds a selfh.st CDN URL for a normalized icon name and format.
  /// </summary>
  /// <param name="iconName">The icon name to normalize and include in the URL.</param>
  /// <param name="format">The desired image format. Unsupported values fall back to <c>png</c>.</param>
  /// <returns>The icon URL, or an empty string when the icon name is blank.</returns>
  public static string BuildIconUrl (string iconName, string format = "png")
  {
    var normalizedIconName = NormalizeIconName (iconName);
    if (string.IsNullOrWhiteSpace (normalizedIconName))
    {
      return "";
    }

    var normalizedFormat = NormalizeFormat (format);
    return $"{SelfhStIconsBaseUrl}/{normalizedFormat}/{normalizedIconName}.{normalizedFormat}";
  }

  /// <summary>
  /// Normalizes a user-provided icon name into the slug format expected by the selfh.st CDN.
  /// </summary>
  /// <param name="iconName">The raw icon name entered by the user.</param>
  /// <returns>A lowercase hyphenated icon name, or an empty string when the input is blank.</returns>
  public static string NormalizeIconName (string iconName)
  {
    if (string.IsNullOrWhiteSpace (iconName))
    {
      return "";
    }

    var normalized = Regex.Replace (iconName.Trim ().ToLowerInvariant (), "[^a-z0-9]+", "-");
    return normalized.Trim ('-');
  }

  /// <summary>
  /// Normalizes an icon format to one supported by the selfh.st CDN.
  /// </summary>
  /// <param name="format">The requested image format.</param>
  /// <returns>A supported format string.</returns>
  private static string NormalizeFormat (string format)
  {
    var normalized = format.Trim ().ToLowerInvariant ();
    return normalized is "svg" or "png" or "webp" ? normalized : "png";
  }
}

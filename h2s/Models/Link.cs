using System.Text.RegularExpressions;

namespace h2s.Models;

public class Link
{
  private const string SelfhStIconsBaseUrl = "https://cdn.jsdelivr.net/gh/selfhst/icons";

  public int Id { get; set; }
  public int CategoryId { get; set; }
  public string Label { get; set; } = "";
  public string Description { get; set; } = "";
  public string IconName { get; set; } = "";
  public string Url { get; set; } = "";

  public Category? Category { get; set; }

  public string GetIconUrl (string format = "png") => BuildIconUrl (this.IconName, format);

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

  public static string NormalizeIconName (string iconName)
  {
    if (string.IsNullOrWhiteSpace (iconName))
    {
      return "";
    }

    var normalized = Regex.Replace (iconName.Trim ().ToLowerInvariant (), "[^a-z0-9]+", "-");
    return normalized.Trim ('-');
  }

  private static string NormalizeFormat (string format)
  {
    var normalized = format.Trim ().ToLowerInvariant ();
    return normalized is "svg" or "png" or "webp" ? normalized : "png";
  }
}

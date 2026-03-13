namespace h2s.Models;

/// <summary>
/// Represents a group of related dashboard links.
/// </summary>
public class Category
{
  /// <summary>
  /// Gets or sets the unique identifier for the category.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  /// Gets or sets the display name shown for the category.
  /// </summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// Gets or sets a value indicating whether the category belongs in the admin section of the dashboard.
  /// </summary>
  public bool IsAdminCategory { get; set; }

  /// <summary>
  /// Gets or sets the links assigned to the category.
  /// </summary>
  public List<Link> Links { get; set; } = new ();
}

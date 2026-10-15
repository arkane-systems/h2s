using h2s.Models;

namespace h2s.Pages.Admin;

/// <summary>
///   Everything needed to recreate a deleted category with its links. The delete handler returns it, and Undo posts it
///   back unchanged.
/// </summary>
/// <param name="Name">The category name.</param>
/// <param name="IsAdminCategory">Whether the category belonged to the admin section.</param>
/// <param name="SortOrder">The category's position within its section.</param>
/// <param name="Links">The category's links, in order.</param>
public sealed record CategorySnapshot (string? Name, bool IsAdminCategory, int SortOrder, List<LinkSnapshot>? Links)
{
  /// <summary>
  ///   Takes a snapshot of a category and its links.
  /// </summary>
  /// <param name="category">The category, with its links loaded.</param>
  /// <returns>The snapshot.</returns>
  public static CategorySnapshot From (Category category)
    => new (Name: category.Name,
            IsAdminCategory: category.IsAdminCategory,
            SortOrder: category.SortOrder,
            Links: [.. category.Links.OrderBy (l => l.SortOrder).ThenBy (l => l.Label).Select (LinkSnapshot.From)]);
}

namespace h2s.Pages.Admin;

/// <summary>
///   A link as the list view shows it.
/// </summary>
/// <param name="Id">The link identifier.</param>
/// <param name="CategoryId">The link's category.</param>
/// <param name="CategoryName">The category's name.</param>
/// <param name="IsAdminCategory">Whether the category is in the admin section.</param>
/// <param name="Label">The link label.</param>
/// <param name="Description">The link description.</param>
/// <param name="IconName">The selfh.st icon name.</param>
/// <param name="IconUrl">The link's own icon image URL.</param>
/// <param name="IconSrc">The icon the dashboard shows: the selfh.st icon, else the icon URL, else empty.</param>
/// <param name="MonitorId">The Uptime Kuma monitor ID, if any.</param>
/// <param name="Url">The link URL.</param>
/// <param name="OpenInNewTab">Whether the link opens in a new browser tab.</param>
/// <param name="SortOrder">The link's position within its category.</param>
public sealed record LinkRow (
  int    Id,
  int    CategoryId,
  string CategoryName,
  bool   IsAdminCategory,
  string Label,
  string Description,
  string IconName,
  string IconUrl,
  string IconSrc,
  int?   MonitorId,
  string Url,
  bool   OpenInNewTab,
  int    SortOrder);

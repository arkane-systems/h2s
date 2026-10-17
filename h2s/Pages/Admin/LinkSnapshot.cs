using h2s.Models;

namespace h2s.Pages.Admin;

/// <summary>
///   Everything needed to recreate a deleted link. The delete handlers return it, and Undo posts it back unchanged.
/// </summary>
/// <param name="CategoryId">The category the link belonged to. Ignored when the link is restored with its category.</param>
/// <param name="Label">The link label.</param>
/// <param name="Description">The link description.</param>
/// <param name="IconName">The selfh.st icon name.</param>
/// <param name="IconUrl">The icon image URL, used when there is no icon name.</param>
/// <param name="MonitorId">The Uptime Kuma monitor ID, if any.</param>
/// <param name="Url">The link URL.</param>
/// <param name="SortOrder">The link's position within its category.</param>
/// <param name="OpenInNewTab">Whether the link opens in a new browser tab.</param>
/// <param name="Widgets">The link's widgets, in order; none when <c>null</c>.</param>
public sealed record LinkSnapshot (
  int                   CategoryId,
  string?               Label,
  string?               Description,
  string?               IconName,
  string?               IconUrl,
  int?                  MonitorId,
  string?               Url,
  int                   SortOrder,
  bool                  OpenInNewTab = false,
  List<WidgetSnapshot>? Widgets      = null)
{
  /// <summary>
  ///   Takes a snapshot of a link.
  /// </summary>
  /// <param name="link">The link, with its widgets loaded.</param>
  /// <returns>The snapshot.</returns>
  public static LinkSnapshot From (Link link)
    => new (CategoryId: link.CategoryId,
            Label: link.Label,
            Description: link.Description,
            IconName: link.IconName,
            IconUrl: link.IconUrl,
            MonitorId: link.MonitorId,
            Url: link.Url,
            SortOrder: link.SortOrder,
            OpenInNewTab: link.OpenInNewTab,
            Widgets: [.. link.Widgets.OrderBy (w => w.SortOrder).ThenBy (w => w.Id).Select (WidgetSnapshot.From)]);
}

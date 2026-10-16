using h2s.Models;
using h2s.Widgets;

namespace h2s.Pages;

/// <summary>
///   The view model for a link's tile on the dashboard (<c>Pages/Shared/_LinkTile.cshtml</c>).
/// </summary>
/// <param name="Link">The link the tile opens.</param>
/// <param name="IsExternal">Whether the link points outside the configured local domains.</param>
/// <param name="StatusBadgeUrl">The Uptime Kuma status badge URL, or <c>null</c> when the tile shows no badge.</param>
public sealed record LinkTile (Link Link, bool IsExternal, string? StatusBadgeUrl)
{
  /// <summary>
  ///   Gets the widgets on the tile, in order.
  /// </summary>
  public IReadOnlyList<WidgetView> Widgets { get; init; } = [];
}

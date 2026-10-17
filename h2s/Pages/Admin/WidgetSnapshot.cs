using h2s.Models;

namespace h2s.Pages.Admin;

/// <summary>
///   Everything needed to recreate a deleted link's widget, as part of its <see cref="LinkSnapshot" />.
/// </summary>
/// <param name="ConnectionId">The connection the widget used; it's left out when that connection no longer exists.</param>
/// <param name="Kind">The widget's kind key within the connection's integration.</param>
/// <param name="OptionsJson">The widget's option values, as stored.</param>
/// <param name="RefreshSeconds">The widget's refresh interval, or <c>null</c> for its kind's default.</param>
public sealed record WidgetSnapshot (int ConnectionId, string? Kind, string? OptionsJson, int? RefreshSeconds)
{
  /// <summary>
  ///   Takes a snapshot of a widget.
  /// </summary>
  /// <param name="widget">The widget.</param>
  /// <returns>The snapshot.</returns>
  public static WidgetSnapshot From (Widget widget)
    => new (ConnectionId: widget.ConnectionId,
            Kind: widget.Kind,
            OptionsJson: widget.OptionsJson,
            RefreshSeconds: widget.RefreshSeconds);
}

using h2s.Models;

namespace h2s.Pages.Admin;

/// <summary>
///   Everything needed to recreate a deleted link's widget, as part of its <see cref="LinkSnapshot" />.
/// </summary>
/// <param name="ConnectionId">The connection the widget used; it's left out when that connection no longer exists.</param>
/// <param name="Kind">The widget's kind key within the connection's integration.</param>
/// <param name="OptionsJson">The widget's option values, as stored.</param>
/// <param name="RefreshSeconds">The widget's refresh interval, or <c>null</c> for its kind's default.</param>
/// <param name="Label">The widget's label, such as a status bar item's name; <c>null</c> for none.</param>
public sealed record WidgetSnapshot (int ConnectionId, string? Kind, string? OptionsJson, int? RefreshSeconds, string? Label = null)
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
            RefreshSeconds: widget.RefreshSeconds,
            Label: widget.Label);
}

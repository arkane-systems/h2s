namespace h2s.Widgets;

/// <summary>
///   A checked widget to store on a link, as <see cref="WidgetForm" /> reads it.
/// </summary>
/// <param name="ExistingId">The saved widget it updates, or <c>null</c> for a new one.</param>
/// <param name="ConnectionId">The connection it uses.</param>
/// <param name="Kind">Its kind's key within the connection's integration.</param>
/// <param name="OptionsJson">Its option values, as stored.</param>
/// <param name="RefreshSeconds">Its refresh interval in seconds, or <c>null</c> for the kind's default.</param>
public sealed record WidgetPlan (int? ExistingId, int ConnectionId, string Kind, string OptionsJson, int? RefreshSeconds);

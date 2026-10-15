namespace h2s.Panels;

/// <summary>
///   The view model for the status bar panel (<c>Pages/Shared/_UptimeStatusBar.cshtml</c>).
/// </summary>
/// <param name="StatusPageUrl">The Uptime Kuma status page the bar links to, or <c>null</c> when it isn't a link.</param>
/// <param name="Items">The bar's items, in order.</param>
public sealed record StatusBarPanelModel (string? StatusPageUrl, IReadOnlyList<StatusBarItem> Items);

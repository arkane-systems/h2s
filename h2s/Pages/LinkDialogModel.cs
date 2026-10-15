using h2s.Models;

namespace h2s.Pages;

/// <summary>
///   The view model for the link dialog (<c>Pages/Shared/_LinkDialog.cshtml</c>).
/// </summary>
/// <param name="Categories">The categories a link can belong to, in display order.</param>
/// <param name="IsUptimeKumaConfigured">Whether to offer the Uptime Kuma monitor ID field.</param>
public sealed record LinkDialogModel (IReadOnlyList<Category> Categories, bool IsUptimeKumaConfigured);

using h2s.Models;

namespace h2s.Pages;

/// <summary>
///   The view model for the link dialog (<c>Pages/Shared/_LinkDialog.cshtml</c>).
/// </summary>
/// <param name="Categories">The categories a link can belong to, in display order.</param>
public sealed record LinkDialogModel (IReadOnlyList<Category> Categories);

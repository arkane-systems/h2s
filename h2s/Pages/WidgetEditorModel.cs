namespace h2s.Pages;

/// <summary>
///   The view model for a dialog's widgets section (<c>Pages/Shared/_WidgetEditor.cshtml</c>), which
///   <c>wwwroot/js/modules/widget-editor.js</c> drives. Where the widgets go, and how many they may be, the dialog sets
///   from script, since a panel dialog's change with the kind of panel.
/// </summary>
/// <param name="IdPrefix">The prefix for the section's element IDs, unique on the page.</param>
/// <param name="Legend">The section's heading, such as "Widgets".</param>
/// <param name="EmptyText">What the section says when there are none.</param>
public sealed record WidgetEditorModel (string IdPrefix, string Legend, string EmptyText);

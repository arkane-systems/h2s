using h2s.Widgets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace h2s.Pages;

/// <summary>
///   Serves one widget, read fresh unless it was read recently, as <c>wwwroot/js/modules/widgets.js</c> refreshes it.
/// </summary>
/// <param name="widgets">The service that reads widgets.</param>
public class WidgetModel (WidgetService widgets) : PageModel
{
  /// <summary>
  ///   Renders a widget.
  /// </summary>
  /// <param name="id">The widget's ID.</param>
  /// <param name="cancellationToken">A token that is cancelled when the request is aborted.</param>
  /// <returns>The widget's HTML, or 404 when it doesn't exist.</returns>
  public async Task<IActionResult> OnGetAsync (int id, CancellationToken cancellationToken)
  {
    WidgetView? view = await widgets.ReadAsync (id: id, cancellationToken: cancellationToken);

    if (view == null)
    {
      return this.NotFound ();
    }

    // Each read is fresh to the browser; the server keeps results for the widget's interval.
    this.Response.Headers.CacheControl = "no-store";

    return this.Partial (viewName: "_Widget", model: view);
  }
}

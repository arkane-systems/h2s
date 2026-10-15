using h2s.Data;
using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages;

/// <summary>
///   Sends a web search to its engine: the search panel's form and quick launch both go through here, so a
///   <c>!shortcut</c> picks the engine without any script.
/// </summary>
public class SearchModel : PageModel
{
  private readonly DashboardContext context;

  /// <summary>
  ///   Initializes a new instance of the <see cref="SearchModel" /> class.
  /// </summary>
  /// <param name="context">The database context holding the search engines.</param>
  public SearchModel (DashboardContext context) => this.context = context;

  /// <summary>
  ///   Redirects a query to its engine's search page.
  /// </summary>
  /// <param name="q">The query as typed, with an optional <c>!shortcut</c> word at its start or end.</param>
  /// <returns>A redirect to the search, or back to the dashboard when the query is blank or there are no engines.</returns>
  public async Task<IActionResult> OnGetAsync (string? q)
  {
    List<SearchEngine> engines = await this.context.SearchEngines
                                           .AsNoTracking ()
                                           .OrderBy (e => e.SortOrder)
                                           .ThenBy (e => e.Name)
                                           .ToListAsync (this.HttpContext.RequestAborted);

    return WebSearch.BuildUrl (engines: engines, query: q) is { } url ? this.Redirect (url) : this.RedirectToPage ("/Index");
  }
}

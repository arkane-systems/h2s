using h2s.Models;
using h2s.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages.Admin;

/// <summary>
///   The Settings → Search handlers (<c>wwwroot/js/modules/search-engines.js</c>). One engine is always the default while
///   there are any: the first one added, the one marked default, or, when the default is deleted, the first left.
/// </summary>
public partial class SettingsModel
{
  /// <summary>
  ///   Gets the search engines the page shows, in order.
  /// </summary>
  public List<SearchEngine> SearchEngines { get; private set; } = new ();

  /// <summary>
  ///   Returns the ordered search engine list used by the settings client script.
  /// </summary>
  /// <returns>A JSON payload describing every engine.</returns>
  public async Task<IActionResult> OnGetSearchEnginesAsync ()
    => new JsonResult ((await this.GetOrderedSearchEnginesAsync ()).Select (SettingsModel.DescribeEngine));

  /// <summary>
  ///   Adds a search engine at the end of the list.
  /// </summary>
  /// <param name="name">The engine's name.</param>
  /// <param name="urlTemplate">The engine's search URL, with <c>{query}</c> where the terms go.</param>
  /// <param name="shortcut">The engine's shortcut, with or without its <c>!</c>; blank for none.</param>
  /// <param name="isDefault">Whether the engine becomes the default. The first engine always does.</param>
  /// <returns>A JSON payload describing the new engine, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateSearchEngineAsync (string? name,
                                                                  string? urlTemplate,
                                                                  string? shortcut,
                                                                  bool    isDefault)
  {
    List<SearchEngine> engines = await this.GetOrderedSearchEnginesAsync (tracking: true);
    var                engine  = new SearchEngine { SortOrder = engines.Count == 0 ? 0 : engines.Max (e => e.SortOrder) + 1 };

    if (SettingsModel.ApplyEngine (engine: engine,
                                   others: engines,
                                   name: name,
                                   urlTemplate: urlTemplate,
                                   shortcut: shortcut,
                                   isDefault: isDefault || (engines.Count == 0)) is { } error)
      return this.BadRequestProblem (error);

    this.context.SearchEngines.Add (engine);
    await this.context.SaveChangesAsync ();

    return new JsonResult (SettingsModel.DescribeEngine (engine));
  }

  /// <summary>
  ///   Changes a search engine.
  /// </summary>
  /// <param name="id">The engine identifier.</param>
  /// <param name="name">The engine's name.</param>
  /// <param name="urlTemplate">The engine's search URL, with <c>{query}</c> where the terms go.</param>
  /// <param name="shortcut">The engine's shortcut, with or without its <c>!</c>; blank for none.</param>
  /// <param name="isDefault">
  ///   Whether the engine becomes the default. The default engine can't stop being it; mark another one instead.
  /// </param>
  /// <returns>
  ///   A JSON payload describing the engine, a 400 problem response when validation fails, or a 404 problem response when
  ///   the engine does not exist.
  /// </returns>
  public async Task<IActionResult> OnPostUpdateSearchEngineAsync (int     id,
                                                                  string? name,
                                                                  string? urlTemplate,
                                                                  string? shortcut,
                                                                  bool    isDefault)
  {
    List<SearchEngine> engines = await this.GetOrderedSearchEnginesAsync (tracking: true);

    if (engines.FirstOrDefault (e => e.Id == id) is not { } engine)
      return this.NotFoundProblem ("Search engine not found.");

    if (engine.IsDefault && !isDefault)
      return this.BadRequestProblem ("This is the default engine. Make another engine the default instead.");

    if (SettingsModel.ApplyEngine (engine: engine,
                                   others: [.. engines.Where (e => e != engine)],
                                   name: name,
                                   urlTemplate: urlTemplate,
                                   shortcut: shortcut,
                                   isDefault: isDefault) is { } error)
      return this.BadRequestProblem (error);

    await this.context.SaveChangesAsync ();

    return new JsonResult (SettingsModel.DescribeEngine (engine));
  }

  /// <summary>
  ///   Deletes a search engine. When it was the default, the first engine left becomes the default.
  /// </summary>
  /// <param name="id">The identifier of the engine to remove.</param>
  /// <returns>
  ///   A JSON payload with the deleted engine's ID and what <see cref="OnPostRestoreSearchEngineAsync" /> needs to put it
  ///   back, or a 404 problem response when it does not exist.
  /// </returns>
  public async Task<IActionResult> OnPostDeleteSearchEngineAsync (int id)
  {
    List<SearchEngine> engines = await this.GetOrderedSearchEnginesAsync (tracking: true);

    if (engines.FirstOrDefault (e => e.Id == id) is not { } engine)
      return this.NotFoundProblem ("Search engine not found.");

    int position = engines.IndexOf (engine);

    this.context.SearchEngines.Remove (engine);
    _ = engines.Remove (engine);

    for (var i = 0; i < engines.Count; i++)
      engines[i].SortOrder = i;

    if (engine.IsDefault && (engines.Count > 0))
      engines[0].IsDefault = true;

    await this.context.SaveChangesAsync ();

    return new JsonResult (new
                           {
                             DeletedId = id,
                             Restore = new
                                       {
                                         engine.Name,
                                         engine.UrlTemplate,
                                         engine.Shortcut,
                                         engine.IsDefault,
                                         SortOrder = position,
                                       },
                           });
  }

  /// <summary>
  ///   Recreates a deleted search engine at its old position, and as the default again if it was, for Undo.
  /// </summary>
  /// <param name="name">The engine's name.</param>
  /// <param name="urlTemplate">The engine's search URL.</param>
  /// <param name="shortcut">The engine's shortcut; blank for none.</param>
  /// <param name="isDefault">Whether the engine was the default.</param>
  /// <param name="sortOrder">The engine's old position.</param>
  /// <returns>A JSON payload describing the recreated engine, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostRestoreSearchEngineAsync (string? name,
                                                                   string? urlTemplate,
                                                                   string? shortcut,
                                                                   bool    isDefault,
                                                                   int     sortOrder)
  {
    List<SearchEngine> engines = await this.GetOrderedSearchEnginesAsync (tracking: true);
    var                engine  = new SearchEngine ();

    if (SettingsModel.ApplyEngine (engine: engine,
                                   others: engines,
                                   name: name,
                                   urlTemplate: urlTemplate,
                                   shortcut: shortcut,
                                   isDefault: isDefault || (engines.Count == 0)) is { } error)
      return this.BadRequestProblem (error);

    engines.Insert (index: Math.Clamp (value: sortOrder, min: 0, max: engines.Count), item: engine);

    for (var i = 0; i < engines.Count; i++)
      engines[i].SortOrder = i;

    this.context.SearchEngines.Add (engine);
    await this.context.SaveChangesAsync ();

    return new JsonResult (SettingsModel.DescribeEngine (engine));
  }

  /// <summary>
  ///   Reorders the search engines.
  /// </summary>
  /// <param name="orderedIds">The IDs of every engine, in the desired order.</param>
  /// <returns>A JSON payload confirming success, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostReorderSearchEnginesAsync (string[]? orderedIds)
  {
    if (!OrderedIdParser.TryParse (values: orderedIds, ids: out List<int>? ids, error: out string? error))
      return this.BadRequestProblem (error);

    List<SearchEngine> engines = await this.GetOrderedSearchEnginesAsync (tracking: true);

    if (ids.Any (id => engines.All (e => e.Id != id)))
      return this.BadRequestProblem ("One or more search engines do not exist.");

    if (ids.Count != engines.Count)
      return this.BadRequestProblem ("The order must include every search engine.");

    for (var i = 0; i < ids.Count; i++)
      engines.First (e => e.Id == ids[i]).SortOrder = i;

    await this.context.SaveChangesAsync ();

    return new JsonResult (new { Success = true });
  }

  /// <summary>
  ///   Checks posted engine fields and, when they're valid, applies them; making an engine the default unmarks the others.
  /// </summary>
  /// <param name="engine">The engine to change.</param>
  /// <param name="others">The other engines, tracked.</param>
  /// <param name="name">The posted name.</param>
  /// <param name="urlTemplate">The posted URL template.</param>
  /// <param name="shortcut">The posted shortcut.</param>
  /// <param name="isDefault">Whether the engine is to be the default.</param>
  /// <returns>What's wrong with the fields, or <c>null</c> when they were applied.</returns>
  private static string? ApplyEngine (SearchEngine       engine,
                                      List<SearchEngine> others,
                                      string?            name,
                                      string?            urlTemplate,
                                      string?            shortcut,
                                      bool               isDefault)
  {
    string trimmedName     = (name        ?? "").Trim ();
    string trimmedTemplate = (urlTemplate ?? "").Trim ();

    if ((SearchEngineRules.CheckName (trimmedName) ?? SearchEngineRules.CheckUrlTemplate (trimmedTemplate)) is { } error)
      return error;

    if (!SearchEngineRules.TryNormalizeShortcut (value: shortcut, shortcut: out string normalizedShortcut))
      return SearchEngineRules.InvalidShortcut;

    if ((normalizedShortcut.Length > 0) && others.Any (e => e.Shortcut == normalizedShortcut))
      return $"Another engine already uses !{normalizedShortcut}.";

    engine.Name        = trimmedName;
    engine.UrlTemplate = trimmedTemplate;
    engine.Shortcut    = normalizedShortcut;

    if (isDefault)
    {
      engine.IsDefault = true;

      foreach (SearchEngine other in others)
        other.IsDefault = false;
    }

    return null;
  }

  /// <summary>
  ///   Describes an engine for the settings client script.
  /// </summary>
  /// <param name="engine">The engine.</param>
  /// <returns>Its fields, as JSON.</returns>
  private static object DescribeEngine (SearchEngine engine)
    => new
       {
         engine.Id,
         engine.Name,
         engine.UrlTemplate,
         engine.Shortcut,
         engine.IsDefault,
         engine.SortOrder,
       };

  /// <summary>
  ///   Loads the search engines in order.
  /// </summary>
  /// <param name="tracking">Whether the engines will be changed.</param>
  /// <returns>The engines.</returns>
  private Task<List<SearchEngine>> GetOrderedSearchEnginesAsync (bool tracking = false)
  {
    IQueryable<SearchEngine> query = tracking ? this.context.SearchEngines : this.context.SearchEngines.AsNoTracking ();

    return query.OrderBy (e => e.SortOrder)
                .ThenBy (e => e.Id)
                .ToListAsync ();
  }
}

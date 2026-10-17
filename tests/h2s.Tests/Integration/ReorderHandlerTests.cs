using System.Net;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for how the editor's JSON handlers order categories and links: the reorder handlers that edit mode calls, and
///   where the create and update handlers place what they add or move.
/// </summary>
public class ReorderHandlerTests
{
  private const string EditorPath = "/Admin/Editor";

  [Fact]
  public async Task ReorderCategories_RenumbersTheSection ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ReorderHandlerTests.GetCategoryIdsAsync (factory);

    HttpResponseMessage response = await ReorderHandlerTests.PostAsync (factory: factory,
                                                                        handler: "ReorderCategories",
                                                                        orderedIds: [ids["Reference"], ids["Media"], ids["Home"]]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: [("Reference", 0), ("Media", 1), ("Home", 2), ("Infrastructure", 0), ("Network", 1)],
                  actual: await ReorderHandlerTests.GetCategoryOrderAsync (factory));
  }

  [Fact]
  public async Task ReorderCategories_RejectsMixedSections ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ReorderHandlerTests.GetCategoryIdsAsync (factory);

    HttpResponseMessage response = await ReorderHandlerTests.PostAsync (factory: factory,
                                                                        handler: "ReorderCategories",
                                                                        orderedIds:
                                                                        [
                                                                          ids["Media"],
                                                                          ids["Home"],
                                                                          ids["Reference"],
                                                                          ids["Network"],
                                                                        ]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Regular and admin categories are ordered separately.");
  }

  [Fact]
  public async Task ReorderCategories_RequiresTheWholeSection ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ReorderHandlerTests.GetCategoryIdsAsync (factory);

    HttpResponseMessage response = await ReorderHandlerTests.PostAsync (factory: factory,
                                                                        handler: "ReorderCategories",
                                                                        orderedIds: [ids["Home"], ids["Media"]]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "The order must include every category in the section.");
  }

  [Fact]
  public async Task ReorderCategories_RejectsRepeatedIds ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ReorderHandlerTests.GetCategoryIdsAsync (factory);

    HttpResponseMessage response = await ReorderHandlerTests.PostAsync (factory: factory,
                                                                        handler: "ReorderCategories",
                                                                        orderedIds: [ids["Home"], ids["Home"], ids["Media"]]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Ordered IDs must not repeat an ID.");
  }

  [Fact]
  public async Task ReorderLinks_RenumbersTheCategory ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ReorderHandlerTests.GetLinkIdsAsync (factory);

    HttpResponseMessage response = await ReorderHandlerTests.PostAsync (factory: factory,
                                                                        handler: "ReorderLinks",
                                                                        categoryId:
                                                                        await ReorderHandlerTests
                                                                         .GetCategoryIdAsync (factory: factory,
                                                                                              name: "Media"),
                                                                        orderedIds:
                                                                        [
                                                                          ids["Audiobookshelf"],
                                                                          ids["Radarr"],
                                                                          ids["Sonarr"],
                                                                          ids["Jellyfin"],
                                                                        ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: ["Audiobookshelf", "Radarr", "Sonarr", "Jellyfin"],
                  actual: await ReorderHandlerTests.GetLinkOrderAsync (factory: factory, categoryName: "Media"));
  }

  [Fact]
  public async Task ReorderLinks_MovesLinksBetweenCategoriesAndClosesTheGap ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ReorderHandlerTests.GetLinkIdsAsync (factory);

    HttpResponseMessage response = await ReorderHandlerTests.PostAsync (factory: factory,
                                                                        handler: "ReorderLinks",
                                                                        categoryId:
                                                                        await ReorderHandlerTests
                                                                         .GetCategoryIdAsync (factory: factory,
                                                                                              name: "Home"),
                                                                        orderedIds:
                                                                        [
                                                                          ids["Home Assistant"],
                                                                          ids["Sonarr"],
                                                                          ids["Frigate"],
                                                                          ids["Paperless-ngx"],
                                                                          ids["Recipes"],
                                                                        ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: ["Home Assistant", "Sonarr", "Frigate", "Paperless-ngx", "Recipes"],
                  actual: await ReorderHandlerTests.GetLinkOrderAsync (factory: factory, categoryName: "Home"));
    Assert.Equal (expected: ["Jellyfin", "Radarr", "Audiobookshelf"],
                  actual: await ReorderHandlerTests.GetLinkOrderAsync (factory: factory, categoryName: "Media"));
    Assert.Equal (expected: [0, 1, 2],
                  actual: await ReorderHandlerTests.GetSortOrdersAsync (factory: factory, categoryName: "Media"));
  }

  [Fact]
  public async Task ReorderLinks_RequiresTheWholeCategory ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ReorderHandlerTests.GetLinkIdsAsync (factory);

    HttpResponseMessage response = await ReorderHandlerTests.PostAsync (factory: factory,
                                                                        handler: "ReorderLinks",
                                                                        categoryId:
                                                                        await ReorderHandlerTests
                                                                         .GetCategoryIdAsync (factory: factory,
                                                                                              name: "Media"),
                                                                        orderedIds: [ids["Sonarr"], ids["Jellyfin"]]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "The order must include every link in the category.");
  }

  [Fact]
  public async Task ReorderLinks_RejectsUnknownLinks ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await ReorderHandlerTests.PostAsync (factory: factory,
                                                                        handler: "ReorderLinks",
                                                                        categoryId:
                                                                        await ReorderHandlerTests
                                                                         .GetCategoryIdAsync (factory: factory,
                                                                                              name: "Media"),
                                                                        orderedIds: [99999]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "One or more links do not exist.");
  }

  [Fact]
  public async Task ReorderLinks_ReturnsNotFoundForUnknownCategory ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ReorderHandlerTests.GetLinkIdsAsync (factory);

    HttpResponseMessage response = await ReorderHandlerTests.PostAsync (factory: factory,
                                                                        handler: "ReorderLinks",
                                                                        categoryId: 99999,
                                                                        orderedIds: [ids["Sonarr"]]);

    await FormPoster.AssertProblemAsync (response: response, status: HttpStatusCode.NotFound, detail: "Category not found.");
  }

  [Fact]
  public async Task CreateCategory_AppendsToItsSection ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: ReorderHandlerTests.EditorPath,
                                                               handler: "CreateCategory",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "name", value: "Backups"),
                                                                 new KeyValuePair<string, string> (key: "isAdminCategory",
                                                                                                   value: "true"),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Contains (expected: ("Backups", 2), collection: await ReorderHandlerTests.GetCategoryOrderAsync (factory));
  }

  [Fact]
  public async Task UpdateCategory_AppendsToANewSection ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: ReorderHandlerTests.EditorPath,
                                                               handler: "UpdateCategory",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "id",
                                                                                                   value:
                                                                                                   (await ReorderHandlerTests
                                                                                                     .GetCategoryIdAsync (factory:
                                                                                                                          factory,
                                                                                                                          name:
                                                                                                                          "Media"))
                                                                                                  .ToString ()),
                                                                 new KeyValuePair<string, string> (key: "name", value: "Media"),
                                                                 new KeyValuePair<string, string> (key: "isAdminCategory",
                                                                                                   value: "true"),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: [("Home", 1), ("Reference", 2), ("Infrastructure", 0), ("Network", 1), ("Media", 2)],
                  actual: await ReorderHandlerTests.GetCategoryOrderAsync (factory));
  }

  [Fact]
  public async Task CreateLink_AppendsToItsCategory ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: ReorderHandlerTests.EditorPath,
                                                               handler: "CreateLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "categoryId",
                                                                                                   value:
                                                                                                   (await ReorderHandlerTests
                                                                                                     .GetCategoryIdAsync (factory:
                                                                                                                          factory,
                                                                                                                          name:
                                                                                                                          "Media"))
                                                                                                  .ToString ()),
                                                                 new KeyValuePair<string, string> (key: "label", value: "Bazarr"),
                                                                 new KeyValuePair<string, string> (key: "url",
                                                                                                   value:
                                                                                                   "https://bazarr.home.arpa/"),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: ["Jellyfin", "Sonarr", "Radarr", "Audiobookshelf", "Bazarr"],
                  actual: await ReorderHandlerTests.GetLinkOrderAsync (factory: factory, categoryName: "Media"));
  }

  [Fact]
  public async Task UpdateLink_AppendsToANewCategory ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ReorderHandlerTests.GetLinkIdsAsync (factory);

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: ReorderHandlerTests.EditorPath,
                                                               handler: "UpdateLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "id",
                                                                                                   value: ids["Jellyfin"]
                                                                                                    .ToString ()),
                                                                 new KeyValuePair<string, string> (key: "categoryId",
                                                                                                   value:
                                                                                                   (await ReorderHandlerTests
                                                                                                     .GetCategoryIdAsync (factory:
                                                                                                                          factory,
                                                                                                                          name:
                                                                                                                          "Home"))
                                                                                                  .ToString ()),
                                                                 new KeyValuePair<string, string> (key: "label", value: "Jellyfin"),
                                                                 new KeyValuePair<string, string> (key: "url",
                                                                                                   value:
                                                                                                   "https://jellyfin.home.arpa/"),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: ["Home Assistant", "Frigate", "Paperless-ngx", "Recipes", "Jellyfin"],
                  actual: await ReorderHandlerTests.GetLinkOrderAsync (factory: factory, categoryName: "Home"));
  }

  /// <summary>
  ///   Posts an ordered ID list to a reorder handler.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="handler">The handler name.</param>
  /// <param name="orderedIds">The IDs, in order.</param>
  /// <param name="categoryId">The category whose links are ordered, for the link handler.</param>
  /// <returns>The response.</returns>
  private static Task<HttpResponseMessage> PostAsync (DashboardApplicationFactory factory,
                                                      string                      handler,
                                                      int[]                       orderedIds,
                                                      int?                        categoryId = null)
  {
    IEnumerable<KeyValuePair<string, string>> fields =
      orderedIds.Select (id => new KeyValuePair<string, string> (key: "orderedIds", value: id.ToString ()));

    if (categoryId.HasValue)
    {
      fields = fields.Prepend (new KeyValuePair<string, string> (key: "categoryId", value: categoryId.Value.ToString ()));
    }

    return FormPoster.PostAsync (client: factory.CreateClient (),
                                 pagePath: ReorderHandlerTests.EditorPath,
                                 handler: handler,
                                 fields: fields);
  }

  /// <summary>
  ///   Gets the ID of the category with the given name.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="name">The category name.</param>
  /// <returns>The category ID.</returns>
  private static async Task<int> GetCategoryIdAsync (DashboardApplicationFactory factory, string name)
    => (await ReorderHandlerTests.GetCategoryIdsAsync (factory))[name];

  /// <summary>
  ///   Gets the ID of every category, by name.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The category IDs.</returns>
  private static async Task<Dictionary<string, int>> GetCategoryIdsAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.Categories.ToDictionaryAsync (keySelector: c => c.Name,
                                                         elementSelector: c => c.Id,
                                                         cancellationToken: TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Gets the ID of every link, by label.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The link IDs.</returns>
  private static async Task<Dictionary<string, int>> GetLinkIdsAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.Links.ToDictionaryAsync (keySelector: l => l.Label,
                                                    elementSelector: l => l.Id,
                                                    cancellationToken: TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Gets every category's name and sort order, regular categories first, each section in sort order.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The categories.</returns>
  private static async Task<List<(string Name, int SortOrder)>> GetCategoryOrderAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return (await context.Categories.OrderBy (c => c.IsAdminCategory)
                           .ThenBy (c => c.SortOrder)
                           .ThenBy (c => c.Name)
                           .Select (c => new { c.Name, c.SortOrder })
                           .ToListAsync (TestContext.Current.CancellationToken))
            .Select (c => (c.Name, c.SortOrder))
            .ToList ();
    }
  }

  /// <summary>
  ///   Gets the labels of a category's links in sort order.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="categoryName">The category name.</param>
  /// <returns>The link labels.</returns>
  private static async Task<List<string>> GetLinkOrderAsync (DashboardApplicationFactory factory, string categoryName)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.Links.Where (l => l.Category!.Name == categoryName)
                          .OrderBy (l => l.SortOrder)
                          .Select (l => l.Label)
                          .ToListAsync (TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Gets the sort orders of a category's links, ascending.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="categoryName">The category name.</param>
  /// <returns>The sort orders.</returns>
  private static async Task<List<int>> GetSortOrdersAsync (DashboardApplicationFactory factory, string categoryName)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.Links.Where (l => l.Category!.Name == categoryName)
                          .OrderBy (l => l.SortOrder)
                          .Select (l => l.SortOrder)
                          .ToListAsync (TestContext.Current.CancellationToken);
    }
  }
}

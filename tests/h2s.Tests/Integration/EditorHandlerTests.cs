using System.Net;
using System.Text.Json;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the JSON page handlers behind the admin editor.
/// </summary>
public class EditorHandlerTests
{
  private const string EditorPath = "/Admin/Editor";

  [Fact]
  public async Task CreateCategory_AddsCategory ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: client,
                                                               pagePath: EditorHandlerTests.EditorPath,
                                                               handler: "CreateCategory",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "name", value: "  Games  "),
                                                                 new KeyValuePair<string, string> (key: "isAdminCategory",
                                                                                                   value: "true"),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    JsonElement json = await FormPoster.ReadJsonAsync (response);

    Assert.Equal (expected: "Games", actual: json.GetProperty ("name").GetString ());
    Assert.True (json.GetProperty ("isAdminCategory").GetBoolean ());

    JsonElement categories =
      await FormPoster.ReadJsonAsync (await client.GetAsync (requestUri: $"{EditorHandlerTests.EditorPath}?handler=Categories",
                                                             cancellationToken: TestContext.Current.CancellationToken));

    Assert.Contains (collection: categories.EnumerateArray (), filter: c => c.GetProperty ("name").GetString () == "Games");
  }

  [Fact]
  public async Task CreateCategory_RejectsBlankName ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: EditorHandlerTests.EditorPath,
                                                               handler: "CreateCategory",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "name", value: "   "),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.BadRequest, actual: response.StatusCode);
  }

  [Fact]
  public async Task CreateLink_AddsLinkWithNormalizedIcon ()
  {
    await using var factory    = new DashboardApplicationFactory ();
    int             categoryId = await EditorHandlerTests.GetCategoryIdAsync (factory: factory, name: "Media");

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: EditorHandlerTests.EditorPath,
                                                               handler: "CreateLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "categoryId",
                                                                                                   value: categoryId.ToString ()),
                                                                 new KeyValuePair<string, string> (key: "label",
                                                                                                   value: "Navidrome"),
                                                                 new KeyValuePair<string, string> (key: "url",
                                                                                                   value:
                                                                                                   "https://music.home.arpa/"),
                                                                 new KeyValuePair<string, string> (key: "iconName",
                                                                                                   value: "Navidrome"),
                                                                 new KeyValuePair<string, string> (key: "monitorId", value: "14"),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    JsonElement json = await FormPoster.ReadJsonAsync (response);

    Assert.Equal (expected: "Media",     actual: json.GetProperty ("categoryName").GetString ());
    Assert.Equal (expected: "navidrome", actual: json.GetProperty ("iconName").GetString ());
    Assert.Equal (expected: 14,          actual: json.GetProperty ("monitorId").GetInt32 ());
  }

  [Fact]
  public async Task CreateLink_StoresBlankMonitorIdAsNone ()
  {
    await using var factory    = new DashboardApplicationFactory ();
    int             categoryId = await EditorHandlerTests.GetCategoryIdAsync (factory: factory, name: "Media");

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: EditorHandlerTests.EditorPath,
                                                               handler: "CreateLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "categoryId",
                                                                                                   value: categoryId.ToString ()),
                                                                 new KeyValuePair<string, string> (key: "label",
                                                                                                   value: "Navidrome"),
                                                                 new KeyValuePair<string, string> (key: "url",
                                                                                                   value:
                                                                                                   "https://music.home.arpa/"),
                                                                 new KeyValuePair<string, string> (key: "monitorId", value: "  "),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: JsonValueKind.Null,
                  actual: (await FormPoster.ReadJsonAsync (response)).GetProperty ("monitorId").ValueKind);
  }

  [Theory]
  [InlineData ("",          "https://music.home.arpa/", "")]
  [InlineData ("Navidrome", "",                         "")]
  [InlineData ("Navidrome", "ftp://music.home.arpa/",   "")]
  [InlineData ("Navidrome", "https://music.home.arpa/", "0")]
  [InlineData ("Navidrome", "https://music.home.arpa/", "-3")]
  [InlineData ("Navidrome", "https://music.home.arpa/", "abc")]
  [InlineData ("Navidrome", "https://music.home.arpa/", "99999999999")]
  public async Task CreateLink_RejectsInvalidInput (string label, string url, string monitorId)
  {
    await using var factory    = new DashboardApplicationFactory ();
    int             categoryId = await EditorHandlerTests.GetCategoryIdAsync (factory: factory, name: "Media");

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: EditorHandlerTests.EditorPath,
                                                               handler: "CreateLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "categoryId",
                                                                                                   value: categoryId.ToString ()),
                                                                 new KeyValuePair<string, string> (key: "label", value: label),
                                                                 new KeyValuePair<string, string> (key: "url",   value: url),
                                                                 new KeyValuePair<string, string> (key: "monitorId",
                                                                                                   value: monitorId),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.BadRequest, actual: response.StatusCode);
  }

  [Fact]
  public async Task DeleteCategory_RemovesItsLinks ()
  {
    await using var factory    = new DashboardApplicationFactory ();
    int             categoryId = await EditorHandlerTests.GetCategoryIdAsync (factory: factory, name: "Reference");

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: EditorHandlerTests.EditorPath,
                                                               handler: "DeleteCategory",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "id",
                                                                                                   value: categoryId.ToString ()),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Assert.False (await context.Categories.AnyAsync (predicate: c => c.Id == categoryId,
                                                       cancellationToken: TestContext.Current.CancellationToken));
      Assert.False (await context.Links.AnyAsync (predicate: l => l.CategoryId == categoryId,
                                                  cancellationToken: TestContext.Current.CancellationToken));
    }
  }

  [Fact]
  public async Task Post_RequiresAntiforgeryToken ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await factory.CreateClient ()
                                                .PostAsync (requestUri: $"{EditorHandlerTests.EditorPath}?handler=CreateCategory",
                                                            content: new FormUrlEncodedContent ([
                                                                                                  new KeyValuePair<string,
                                                                                                    string> (key: "name",
                                                                                                             value: "Sneaky"),
                                                                                                ]),
                                                            cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.BadRequest, actual: response.StatusCode);
  }

  [Fact]
  public async Task UpdateLink_ReturnsNotFoundForUnknownLink ()
  {
    await using var factory    = new DashboardApplicationFactory ();
    int             categoryId = await EditorHandlerTests.GetCategoryIdAsync (factory: factory, name: "Media");

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: EditorHandlerTests.EditorPath,
                                                               handler: "UpdateLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "id", value: "99999"),
                                                                 new KeyValuePair<string, string> (key: "categoryId",
                                                                                                   value: categoryId.ToString ()),
                                                                 new KeyValuePair<string, string> (key: "label", value: "Ghost"),
                                                                 new KeyValuePair<string, string> (key: "url",
                                                                                                   value:
                                                                                                   "https://ghost.home.arpa/"),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.NotFound, actual: response.StatusCode);
  }

  /// <summary>
  ///   Looks up a seeded category's ID by name.
  /// </summary>
  /// <param name="factory">The factory hosting the database.</param>
  /// <param name="name">The category name.</param>
  /// <returns>The category ID.</returns>
  private static async Task<int> GetCategoryIdAsync (DashboardApplicationFactory factory, string name)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.Categories.Where (c => c.Name == name)
                          .Select (c => c.Id)
                          .SingleAsync (TestContext.Current.CancellationToken);
    }
  }
}

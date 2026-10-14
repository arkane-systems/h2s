using System.Net;
using System.Text;
using System.Text.Json;
using h2s.Models;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the editor handlers behind the link and category dialogs in dashboard edit mode: suggestions, previews,
///   refreshed tiles, and deleting with Undo.
/// </summary>
public class LinkDialogHandlerTests
{
  private const string EditorPath = "/Admin/Editor";

  [Fact]
  public async Task SuggestLink_SuggestsTheTitleAndAnIconFromIt ()
  {
    await using var factory = new DashboardApplicationFactory ();
    factory.Outbound = request => request.RequestUri == new Uri ("https://music.home.arpa/")
                                    ? LinkDialogHandlerTests.Html ("<title>Navidrome</title>")
                                    : DashboardApplicationFactory.ServeIconIndex (request);

    JsonElement json = await LinkDialogHandlerTests.GetJsonAsync (factory: factory,
                                                                  query:
                                                                  "handler=SuggestLink&url=https%3A%2F%2Fmusic.home.arpa%2F");

    Assert.Equal (expected: "Navidrome", actual: json.GetProperty ("title").GetString ());
    Assert.Equal (expected: "navidrome", actual: json.GetProperty ("iconName").GetString ());
  }

  [Fact]
  public async Task SuggestLink_FallsBackToTheHostForTheIcon ()
  {
    await using var factory = new DashboardApplicationFactory ();

    // The page itself can't be read (the stub answers 404), but its host name still names an icon.
    JsonElement json = await LinkDialogHandlerTests.GetJsonAsync (factory: factory,
                                                                  query:
                                                                  "handler=SuggestLink&url=https%3A%2F%2Fjellyfin.home.arpa%2Fweb%2F");

    Assert.Equal (expected: JsonValueKind.Null, actual: json.GetProperty ("title").ValueKind);
    Assert.Equal (expected: "jellyfin",         actual: json.GetProperty ("iconName").GetString ());
  }

  [Fact]
  public async Task TilePreview_RendersTheDashboardTile ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri:
                                                $"{LinkDialogHandlerTests.EditorPath}?handler=TilePreview&label=Docs&url=https%3A%2F%2Fexample.com%2F&iconName=GitHub&description=Read+me",
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """<span class="link-block-title">Docs</span>""",           actualString: html);
    Assert.Contains (expectedSubstring: """<span class="link-block-description">Read me</span>""",  actualString: html);
    Assert.Contains (expectedSubstring: "https://cdn.jsdelivr.net/gh/selfhst/icons/png/github.png", actualString: html);
    Assert.Contains (expectedSubstring: "link-external-flag",                                       actualString: html);
    Assert.DoesNotContain (expectedSubstring: "<html", actualString: html);
  }

  [Fact]
  public async Task LinkSlot_RendersTheEditModeTile ()
  {
    await using var factory = new DashboardApplicationFactory ();
    int             linkId  = await LinkDialogHandlerTests.GetLinkIdAsync (factory: factory, label: "Sonarr");

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: $"{LinkDialogHandlerTests.EditorPath}?handler=LinkSlot&id={linkId}",
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.StartsWith (expectedStartString: $"""<div class="link-slot" data-link-id="{linkId}">""", actualString: html.Trim ());
    Assert.Contains (expectedSubstring: "link-drag-handle",                                 actualString: html);
    Assert.Contains (expectedSubstring: """<span class="link-block-title">Sonarr</span>""", actualString: html);
  }

  [Fact]
  public async Task LinkSlot_ReturnsNotFoundForUnknownLink ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await factory.CreateClient ()
                                                .GetAsync (requestUri:
                                                           $"{LinkDialogHandlerTests.EditorPath}?handler=LinkSlot&id=99999",
                                                           cancellationToken: TestContext.Current.CancellationToken);

    await FormPoster.AssertProblemAsync (response: response, status: HttpStatusCode.NotFound, detail: "Link not found.");
  }

  [Fact]
  public async Task Link_ReturnsTheLinkForEditing ()
  {
    await using var factory = new DashboardApplicationFactory ();
    int             linkId  = await LinkDialogHandlerTests.GetLinkIdAsync (factory: factory, label: "Jellyfin");

    JsonElement json = await LinkDialogHandlerTests.GetJsonAsync (factory: factory, query: $"handler=Link&id={linkId}");

    Assert.Equal (expected: "Jellyfin",                    actual: json.GetProperty ("label").GetString ());
    Assert.Equal (expected: "https://jellyfin.home.arpa/", actual: json.GetProperty ("url").GetString ());
    Assert.Equal (expected: 11,                            actual: json.GetProperty ("monitorId").GetInt32 ());
  }

  [Fact]
  public async Task DeleteLink_ThenRestoreLink_PutsItBackWhereItWas ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();
    int             linkId  = await LinkDialogHandlerTests.GetLinkIdAsync (factory: factory, label: "Sonarr");

    JsonElement deleted = await LinkDialogHandlerTests.PostJsonAsync (client: client,
                                                                      handler: "DeleteLink",
                                                                      fields:
                                                                      [
                                                                        new KeyValuePair<string, string> (key: "id",
                                                                                                          value: linkId
                                                                                                           .ToString ()),
                                                                      ]);
    JsonElement restore = deleted.GetProperty ("restore");

    Assert.Equal (expected: "Sonarr", actual: restore.GetProperty ("label").GetString ());
    Assert.Equal (expected: 1,        actual: restore.GetProperty ("sortOrder").GetInt32 ());

    JsonElement restored = await LinkDialogHandlerTests.PostJsonAsync (client: client,
                                                                       handler: "RestoreLink",
                                                                       fields:
                                                                       [
                                                                         new KeyValuePair<string, string> (key: "snapshot",
                                                                                                           value: restore
                                                                                                            .GetRawText ()),
                                                                       ]);

    Assert.NotEqual (expected: linkId, actual: restored.GetProperty ("id").GetInt32 ());
    Assert.Equal (expected: ["Jellyfin", "Sonarr", "Radarr", "Audiobookshelf"],
                  actual: await LinkDialogHandlerTests.GetLinkOrderAsync (factory: factory, categoryName: "Media"));

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Link sonarr = await context.Links.SingleAsync (predicate: l => l.Label == "Sonarr",
                                                     cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal (expected: 12,                          actual: sonarr.MonitorId);
      Assert.Equal (expected: "TV series manager",         actual: sonarr.Description);
      Assert.Equal (expected: "https://sonarr.home.arpa/", actual: sonarr.Url);
    }
  }

  [Fact]
  public async Task RestoreLink_ReturnsNotFoundWhenTheCategoryIsGone ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: LinkDialogHandlerTests.EditorPath,
                                                               handler: "RestoreLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "snapshot",
                                                                                                   value:
                                                                                                   """{"categoryId":99999,"label":"Lost","url":"https://lost.home.arpa/","sortOrder":0}"""),
                                                               ]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.NotFound,
                                         detail: "The link's category no longer exists.");
  }

  [Theory]
  [InlineData ("", "The link to restore is missing or malformed.")]
  [InlineData ("{not json", "The link to restore is missing or malformed.")]
  [InlineData ("""{"categoryId":CATEGORY,"label":"","url":"https://x.home.arpa/"}""", "Link label is required.")]
  [InlineData ("""{"categoryId":CATEGORY,"label":"X","url":"ftp://x.home.arpa/"}""", "Link URL must be a valid HTTP or HTTPS URL.")]
  [InlineData ("""{"categoryId":CATEGORY,"label":"X","url":"https://x.home.arpa/","monitorId":0}""",
               "Monitor ID must be a positive integer.")]
  public async Task RestoreLink_RejectsInvalidSnapshots (string snapshot, string detail)
  {
    await using var factory    = new DashboardApplicationFactory ();
    int             categoryId = await LinkDialogHandlerTests.GetCategoryIdAsync (factory: factory, name: "Media");

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: LinkDialogHandlerTests.EditorPath,
                                                               handler: "RestoreLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "snapshot",
                                                                                                   value: snapshot
                                                                                                    .Replace (oldValue: "CATEGORY",
                                                                                                              newValue: categoryId
                                                                                                               .ToString ())),
                                                               ]);

    await FormPoster.AssertProblemAsync (response: response, status: HttpStatusCode.BadRequest, detail: detail);
  }

  [Fact]
  public async Task DeleteCategory_ThenRestoreCategory_PutsItAndItsLinksBack ()
  {
    await using var factory    = new DashboardApplicationFactory ();
    HttpClient      client     = factory.CreateClient ();
    int             categoryId = await LinkDialogHandlerTests.GetCategoryIdAsync (factory: factory, name: "Home");

    JsonElement deleted = await LinkDialogHandlerTests.PostJsonAsync (client: client,
                                                                      handler: "DeleteCategory",
                                                                      fields:
                                                                      [
                                                                        new KeyValuePair<string, string> (key: "id",
                                                                                                          value: categoryId
                                                                                                           .ToString ()),
                                                                      ]);

    _ = await LinkDialogHandlerTests.PostJsonAsync (client: client,
                                                    handler: "RestoreCategory",
                                                    fields:
                                                    [
                                                      new KeyValuePair<string, string> (key: "snapshot",
                                                                                        value: deleted.GetProperty ("restore")
                                                                                                      .GetRawText ()),
                                                    ]);

    string html = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.True (html.IndexOf (value: ">Media</h2>", comparisonType: StringComparison.Ordinal) <
                 html.IndexOf (value: ">Home</h2>",  comparisonType: StringComparison.Ordinal));
    Assert.True (html.IndexOf (value: ">Home</h2>",      comparisonType: StringComparison.Ordinal) <
                 html.IndexOf (value: ">Reference</h2>", comparisonType: StringComparison.Ordinal));
    Assert.Equal (expected: ["Home Assistant", "Frigate", "Paperless-ngx", "Recipes"],
                  actual: await LinkDialogHandlerTests.GetLinkOrderAsync (factory: factory, categoryName: "Home"));
  }

  [Fact]
  public async Task Index_EditModeRendersTheDialogsAndAddButtons ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/?edit=true",
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """id="link-dialog""",                                     actualString: html);
    Assert.Contains (expectedSubstring: """id="category-dialog""",                                 actualString: html);
    Assert.Contains (expectedSubstring: """data-suggest-url="/Admin/Editor?handler=SuggestLink""", actualString: html);
    Assert.Contains (expectedSubstring: """data-role="add-category""",                             actualString: html);
    Assert.Equal (expected: 5, actual: html.Split ("""data-role="add-link""").Length      - 1);
    Assert.Equal (expected: 5, actual: html.Split ("""data-role="edit-category""").Length - 1);

    // Inside .app-content, whose z-index makes a stacking context, a modal would sit beneath Bootstrap's backdrop.
    Assert.True (html.IndexOf (value: "</main>",             comparisonType: StringComparison.Ordinal) <
                 html.IndexOf (value: """id="link-dialog""", comparisonType: StringComparison.Ordinal));
  }

  /// <summary>
  ///   Answers with an HTML page.
  /// </summary>
  /// <param name="html">The page.</param>
  /// <returns>The response.</returns>
  private static HttpResponseMessage Html (string html)
    => new (HttpStatusCode.OK) { Content = new StringContent (content: html, encoding: Encoding.UTF8, mediaType: "text/html") };

  /// <summary>
  ///   Gets JSON from an editor handler.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="query">The query string, including the handler.</param>
  /// <returns>The JSON.</returns>
  private static async Task<JsonElement> GetJsonAsync (DashboardApplicationFactory factory, string query)
  {
    HttpResponseMessage response = await factory.CreateClient ()
                                                .GetAsync (requestUri: $"{LinkDialogHandlerTests.EditorPath}?{query}",
                                                           cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return await FormPoster.ReadJsonAsync (response);
  }

  /// <summary>
  ///   Posts to an editor handler and returns its JSON, asserting success.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="handler">The handler name.</param>
  /// <param name="fields">The form fields.</param>
  /// <returns>The JSON.</returns>
  private static async Task<JsonElement> PostJsonAsync (HttpClient client, string handler, KeyValuePair<string, string>[] fields)
  {
    HttpResponseMessage response = await FormPoster.PostAsync (client: client,
                                                               pagePath: LinkDialogHandlerTests.EditorPath,
                                                               handler: handler,
                                                               fields: fields);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return await FormPoster.ReadJsonAsync (response);
  }

  /// <summary>
  ///   Gets the ID of the link with the given label.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="label">The link label.</param>
  /// <returns>The link ID.</returns>
  private static async Task<int> GetLinkIdAsync (DashboardApplicationFactory factory, string label)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.Links.Where (l => l.Label == label)
                          .Select (l => l.Id)
                          .SingleAsync (TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Gets the ID of the category with the given name.
  /// </summary>
  /// <param name="factory">The application factory.</param>
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
}

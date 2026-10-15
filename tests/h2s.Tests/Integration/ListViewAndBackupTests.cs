using System.Net;
using System.Text;
using System.Text.Json;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the list view (<c>/Admin/Editor</c>), its bulk handlers, the navbar that leads to it, and Settings → Backup.
/// </summary>
public class ListViewAndBackupTests
{
  private const string EditorPath   = "/Admin/Editor";
  private const string SettingsPath = "/Admin/Settings";

  /// <summary>
  ///   A small, valid dashboard file.
  /// </summary>
  private const string SmallBackup = """
                                     {
                                       "format": "h2s-dashboard",
                                       "version": 1,
                                       "settings": { "title": "Imported", "colorMode": "Light", "uptimeKumaDefaultDuration": 24 },
                                       "categories": [
                                         { "name": "Bookmarks", "isAdminCategory": false, "links": [ { "label": "Wiki", "url": "https://wiki.home.arpa/" } ] }
                                       ],
                                       "infrastructureGroups": []
                                     }
                                     """;

  [Fact]
  public async Task ListView_RendersTheTableDataAndTheLinkDialog ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: ListViewAndBackupTests.EditorPath,
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """<h1 class="h4 mb-0 me-auto">Links</h1>""",        actualString: html);
    Assert.Contains (expectedSubstring: """id="link-list-data""",                            actualString: html);
    Assert.Contains (expectedSubstring: "\"moveLinks\":\"/Admin/Editor?handler=MoveLinks\"", actualString: html);
    Assert.Contains (expectedSubstring: """id="link-dialog""",                               actualString: html);
    Assert.Matches (expectedRegexPattern: """<script type="module" src="/js/modules/link-list\.[^"]*js""", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "editor.js", actualString: html);

    // The navbar offers Done, back to the dashboard, rather than Edit.
    Assert.Contains (expectedSubstring: """title="Close the list view""", actualString: html);
    Assert.Contains (expectedSubstring: ">Done</a>",                      actualString: html);
    Assert.DoesNotContain (expectedSubstring: ">Edit</a>", actualString: html);
  }

  [Fact]
  public async Task Links_IncludeTheShownIconAndPosition ()
  {
    await using var factory = new DashboardApplicationFactory ();

    JsonElement links = await FormPoster.ReadJsonAsync (await factory.CreateClient ()
                                                                     .GetAsync (requestUri:
                                                                                $"{ListViewAndBackupTests.EditorPath}?handler=Links",
                                                                                cancellationToken: TestContext.Current
                                                                                                              .CancellationToken));

    JsonElement docker = links.EnumerateArray ().Single (l => l.GetProperty ("label").GetString () == "Docker Hub");
    JsonElement sonarr = links.EnumerateArray ().Single (l => l.GetProperty ("label").GetString () == "Sonarr");

    Assert.Equal (expected: "https://hub.docker.com/favicon.ico", actual: docker.GetProperty ("iconSrc").GetString ());
    Assert.Equal (expected: 1,                                    actual: sonarr.GetProperty ("sortOrder").GetInt32 ());
  }

  [Fact]
  public async Task MoveLinks_AppendsThemInOrderAndClosesTheGaps ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ListViewAndBackupTests.GetLinkIdsAsync (factory);

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: ListViewAndBackupTests.EditorPath,
                                                               handler: "MoveLinks",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "categoryId",
                                                                                                   value:
                                                                                                   (await ListViewAndBackupTests
                                                                                                     .GetCategoryIdAsync (factory:
                                                                                                                          factory,
                                                                                                                          name:
                                                                                                                          "Home"))
                                                                                                  .ToString ()),
                                                                 new KeyValuePair<string, string> (key: "orderedIds",
                                                                                                   value: ids["Radarr"]
                                                                                                    .ToString ()),
                                                                 new KeyValuePair<string, string> (key: "orderedIds",
                                                                                                   value: ids["Jellyfin"]
                                                                                                    .ToString ()),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: ["Home Assistant", "Frigate", "Paperless-ngx", "Recipes", "Radarr", "Jellyfin"],
                  actual: await ListViewAndBackupTests.GetLinkOrderAsync (factory: factory, categoryName: "Home"));
    Assert.Equal (expected: [(0, "Sonarr"), (1, "Audiobookshelf")],
                  actual: await ListViewAndBackupTests.GetPositionsAsync (factory: factory, categoryName: "Media"));
  }

  [Fact]
  public async Task MoveLinks_ReturnsNotFoundForUnknownCategory ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await ListViewAndBackupTests.GetLinkIdsAsync (factory);

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: ListViewAndBackupTests.EditorPath,
                                                               handler: "MoveLinks",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "categoryId",
                                                                                                   value: "99999"),
                                                                 new KeyValuePair<string, string> (key: "orderedIds",
                                                                                                   value: ids["Radarr"]
                                                                                                    .ToString ()),
                                                               ]);

    await FormPoster.AssertProblemAsync (response: response, status: HttpStatusCode.NotFound, detail: "Category not found.");
  }

  [Fact]
  public async Task DeleteLinks_ThenRestoringEachSnapshot_PutsThemBack ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    HttpClient              client  = factory.CreateClient ();
    Dictionary<string, int> ids     = await ListViewAndBackupTests.GetLinkIdsAsync (factory);

    HttpResponseMessage deleted = await FormPoster.PostAsync (client: client,
                                                              pagePath: ListViewAndBackupTests.EditorPath,
                                                              handler: "DeleteLinks",
                                                              fields:
                                                              [
                                                                new KeyValuePair<string, string> (key: "ids",
                                                                                                  value: ids["Radarr"].ToString ()),
                                                                new KeyValuePair<string, string> (key: "ids",
                                                                                                  value: ids["Sonarr"].ToString ()),
                                                                new KeyValuePair<string, string> (key: "ids",
                                                                                                  value: ids["Frigate"]
                                                                                                   .ToString ()),
                                                              ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: deleted.StatusCode);
    Assert.Equal (expected: ["Jellyfin", "Audiobookshelf"],
                  actual: await ListViewAndBackupTests.GetLinkOrderAsync (factory: factory, categoryName: "Media"));

    foreach (JsonElement snapshot in (await FormPoster.ReadJsonAsync (deleted)).GetProperty ("restore").EnumerateArray ())
    {
      HttpResponseMessage restored = await FormPoster.PostAsync (client: client,
                                                                 pagePath: ListViewAndBackupTests.EditorPath,
                                                                 handler: "RestoreLink",
                                                                 fields:
                                                                 [
                                                                   new KeyValuePair<string, string> (key: "snapshot",
                                                                                                     value: snapshot.GetRawText ()),
                                                                 ]);
      Assert.Equal (expected: HttpStatusCode.OK, actual: restored.StatusCode);
    }

    Assert.Equal (expected: ["Jellyfin", "Sonarr", "Radarr", "Audiobookshelf"],
                  actual: await ListViewAndBackupTests.GetLinkOrderAsync (factory: factory, categoryName: "Media"));
    Assert.Equal (expected: ["Home Assistant", "Frigate", "Paperless-ngx", "Recipes"],
                  actual: await ListViewAndBackupTests.GetLinkOrderAsync (factory: factory, categoryName: "Home"));
  }

  [Fact]
  public async Task Navbar_PutsEditLastAndReachesTheListViewFromEditMode ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    string page = await client.GetStringAsync (requestUri: "/",           cancellationToken: TestContext.Current.CancellationToken);
    string edit = await client.GetStringAsync (requestUri: "/?edit=true", cancellationToken: TestContext.Current.CancellationToken);

    Assert.DoesNotContain (expectedSubstring: """href="/Admin/Editor""", actualString: page);
    Assert.True (page.IndexOf (value: """href="/Admin/Settings""", comparisonType: StringComparison.Ordinal) <
                 page.IndexOf (value: """href="/?edit=true""",     comparisonType: StringComparison.Ordinal));
    Assert.Contains (expectedSubstring: """href="/Admin/Editor""", actualString: edit);
  }

  [Fact]
  public async Task Export_DownloadsTheConfiguration ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await factory.CreateClient ()
                                                .GetAsync (requestUri: $"{ListViewAndBackupTests.SettingsPath}?handler=Export",
                                                           cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK,  actual: response.StatusCode);
    Assert.Equal (expected: "application/json", actual: response.Content.Headers.ContentType?.MediaType);
    Assert.StartsWith (expectedStartString: "h2s-dashboard-",
                       actualString: response.Content.Headers.ContentDisposition?.FileNameStar);

    JsonElement json = await FormPoster.ReadJsonAsync (response);

    Assert.Equal (expected: "h2s-dashboard", actual: json.GetProperty ("format").GetString ());
    Assert.Equal (expected: 5,               actual: json.GetProperty ("categories").GetArrayLength ());
    Assert.Equal (expected: "Auto",          actual: json.GetProperty ("settings").GetProperty ("colorMode").GetString ());
  }

  [Fact]
  public async Task PreviewImport_SummarizesWithoutChangingAnything ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    HttpResponseMessage response = await FormPoster.PostFileAsync (client: client,
                                                                   pagePath: ListViewAndBackupTests.SettingsPath,
                                                                   handler: "PreviewImport",
                                                                   fieldName: "file",
                                                                   fileName: "backup.json",
                                                                   contents: Encoding.UTF8.GetBytes (ListViewAndBackupTests
                                                                                                      .SmallBackup));

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    JsonElement summary = await FormPoster.ReadJsonAsync (response);

    Assert.Equal (expected: "Imported", actual: summary.GetProperty ("incoming").GetProperty ("title").GetString ());
    Assert.Equal (expected: 1,          actual: summary.GetProperty ("incoming").GetProperty ("links").GetInt32 ());
    Assert.Equal (expected: 5,          actual: summary.GetProperty ("current").GetProperty ("categories").GetInt32 ());
    Assert.Contains (expectedSubstring: "Development",
                     actualString: await client.GetStringAsync (requestUri: "/",
                                                                cancellationToken: TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task PreviewImport_ListsTheFilesProblems ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostFileAsync (client: factory.CreateClient (),
                                                                   pagePath: ListViewAndBackupTests.SettingsPath,
                                                                   handler: "PreviewImport",
                                                                   fieldName: "file",
                                                                   fileName: "backup.json",
                                                                   contents:
                                                                   Encoding.UTF8
                                                                           .GetBytes ("""{ "format": "h2s-dashboard", "version": 1, "categories": [ { "name": "" } ] }"""));

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "The file has no settings.\nCategory 1: the name is required.");
  }

  [Fact]
  public async Task Import_ReplacesTheConfigurationAndShowsTheNewSettings ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    // Read the settings first, so the cached copy must be replaced for the new title to show.
    _ = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    HttpResponseMessage response = await FormPoster.PostFileAsync (client: client,
                                                                   pagePath: ListViewAndBackupTests.SettingsPath,
                                                                   handler: "Import",
                                                                   fieldName: "file",
                                                                   fileName: "backup.json",
                                                                   contents: Encoding.UTF8.GetBytes (ListViewAndBackupTests
                                                                                                      .SmallBackup));

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    string html = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "Imported",                           actualString: html);
    Assert.Contains (expectedSubstring: "\"category-name\">Bookmarks</span>", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "\"category-name\">Media</span>", actualString: html);
  }

  [Fact]
  public async Task Import_RequiresAFile ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: ListViewAndBackupTests.SettingsPath,
                                                               handler: "Import",
                                                               fields: []);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Choose a dashboard file to import.");
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
    => [.. (await ListViewAndBackupTests.GetPositionsAsync (factory: factory, categoryName: categoryName)).Select (p => p.Label)];

  /// <summary>
  ///   Gets each link's sort order and label in a category, in sort order.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="categoryName">The category name.</param>
  /// <returns>The positions.</returns>
  private static async Task<List<(int SortOrder, string Label)>> GetPositionsAsync (DashboardApplicationFactory factory,
                                                                                    string                      categoryName)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return (await context.Links.Where (l => l.Category!.Name == categoryName)
                           .OrderBy (l => l.SortOrder)
                           .Select (l => new { l.SortOrder, l.Label })
                           .ToListAsync (TestContext.Current.CancellationToken))
            .Select (l => (l.SortOrder, l.Label))
            .ToList ();
    }
  }
}

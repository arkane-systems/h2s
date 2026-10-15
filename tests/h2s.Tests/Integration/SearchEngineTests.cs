using System.Net;
using System.Text.Json;
using h2s.Models;
using h2s.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for search engines: the <c>/Search</c> redirect, the search panel's form, and the Settings → Search handlers.
/// </summary>
public class SearchEngineTests
{
  private const string SettingsPath = "/Admin/Settings";

  [Theory]
  [InlineData ("/Search?q=home+lab",      "https://www.bing.com/search?q=home%20lab")]
  [InlineData ("/Search?q=%21g+home+lab", "https://www.google.com/search?q=home%20lab")]
  [InlineData ("/Search?q=",              "/")]
  [InlineData ("/Search",                 "/")]
  public async Task Search_RedirectsToTheEngine (string path, string location)
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient (new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    using HttpResponseMessage response =
      await client.GetAsync (requestUri: path, cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.Redirect, actual: response.StatusCode);
    Assert.Equal (expected: location,                actual: response.Headers.Location?.OriginalString);
  }

  [Fact]
  public async Task Index_SearchPanelGoesThroughSearch ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """<form class="home-search-form" method="get" role="search" action="/Search">""",
                     actualString: html);
    Assert.Contains (expectedSubstring: """placeholder="Search with Bing""", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "bing.com", actualString: html);
  }

  [Fact]
  public async Task Settings_ListsTheEngines ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: SearchEngineTests.SettingsPath,
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """id="search-engines-data""",      actualString: html);
    Assert.Contains (expectedSubstring: "\"shortcut\":\"g\"",               actualString: html);
    Assert.Contains (expectedSubstring: """<a href="#search">Search</a>""", actualString: html);
  }

  [Fact]
  public async Task CreateSearchEngine_CanTakeOverTheDefault ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await SearchEngineTests.PostAsync (factory: factory,
                                                                      handler: "CreateSearchEngine",
                                                                      ("name", " Wikipedia "),
                                                                      ("urlTemplate",
                                                                       "https://en.wikipedia.org/w/index.php?search={query}"),
                                                                      ("shortcut", "!W"),
                                                                      ("isDefault", "true"));

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected:
                  [
                    ("Bing", "b", false, 0), ("DuckDuckGo", "d", false, 1), ("Google", "g", false, 2), ("Wikipedia", "w", true, 3),
                  ],
                  actual: await SearchEngineTests.GetEnginesAsync (factory));
  }

  [Theory]
  [InlineData ("",  "https://x.example/?q={query}", "",  "A name is required.")]
  [InlineData ("X", "https://x.example/?q=",        "",  "The search URL must contain {query} where the search terms go.")]
  [InlineData ("X", "https://x.example/?q={query}", "G", "Another engine already uses !g.")]
  [InlineData ("X",
               "https://x.example/?q={query}",
               "a b",
               "The shortcut may only use letters, numbers and hyphens, at most 20 of them.")]
  public async Task CreateSearchEngine_ChecksItsFields (string name, string urlTemplate, string shortcut, string detail)
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await SearchEngineTests.PostAsync (factory: factory,
                                                                      handler: "CreateSearchEngine",
                                                                      ("name", name),
                                                                      ("urlTemplate", urlTemplate),
                                                                      ("shortcut", shortcut));

    await FormPoster.AssertProblemAsync (response: response, status: HttpStatusCode.BadRequest, detail: detail);
  }

  [Fact]
  public async Task UpdateSearchEngine_KeepsADefault ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await SearchEngineTests.GetIdsAsync (factory);

    HttpResponseMessage unmark = await SearchEngineTests.PostAsync (factory: factory,
                                                                    handler: "UpdateSearchEngine",
                                                                    ("id", ids["Bing"].ToString ()),
                                                                    ("name", "Bing"),
                                                                    ("urlTemplate", "https://www.bing.com/search?q={query}"),
                                                                    ("shortcut", "b"),
                                                                    ("isDefault", "false"));

    await FormPoster.AssertProblemAsync (response: unmark,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "This is the default engine. Make another engine the default instead.");

    HttpResponseMessage takeOver = await SearchEngineTests.PostAsync (factory: factory,
                                                                      handler: "UpdateSearchEngine",
                                                                      ("id", ids["DuckDuckGo"].ToString ()),
                                                                      ("name", "DDG"),
                                                                      ("urlTemplate", "https://duckduckgo.com/?q={query}"),
                                                                      ("shortcut", ""),
                                                                      ("isDefault", "true"));

    Assert.Equal (expected: HttpStatusCode.OK, actual: takeOver.StatusCode);
    Assert.Equal (expected: [("Bing", "b", false, 0), ("DDG", "", true, 1), ("Google", "g", false, 2)],
                  actual: await SearchEngineTests.GetEnginesAsync (factory));
  }

  [Fact]
  public async Task DeleteThenRestore_HandsTheDefaultOnAndBack ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await SearchEngineTests.GetIdsAsync (factory);

    JsonElement deleted = await FormPoster.ReadJsonAsync (await SearchEngineTests.PostAsync (factory: factory,
                                                                                             handler: "DeleteSearchEngine",
                                                                                             ("id", ids["Bing"].ToString ())));

    Assert.Equal (expected: [("DuckDuckGo", "d", true, 0), ("Google", "g", false, 1)],
                  actual: await SearchEngineTests.GetEnginesAsync (factory));

    JsonElement restore = deleted.GetProperty ("restore");
    HttpResponseMessage restored = await SearchEngineTests.PostAsync (factory: factory,
                                                                      handler: "RestoreSearchEngine",
                                                                      ("name", restore.GetProperty ("name").GetString ()!),
                                                                      ("urlTemplate",
                                                                       restore.GetProperty ("urlTemplate").GetString ()!),
                                                                      ("shortcut", restore.GetProperty ("shortcut").GetString ()!),
                                                                      ("isDefault",
                                                                       restore.GetProperty ("isDefault").GetBoolean ()
                                                                         ? "true"
                                                                         : "false"),
                                                                      ("sortOrder",
                                                                       restore.GetProperty ("sortOrder").GetInt32 ().ToString ()));

    Assert.Equal (expected: HttpStatusCode.OK, actual: restored.StatusCode);
    Assert.Equal (expected: [("Bing", "b", true, 0), ("DuckDuckGo", "d", false, 1), ("Google", "g", false, 2)],
                  actual: await SearchEngineTests.GetEnginesAsync (factory));
  }

  [Fact]
  public async Task ReorderSearchEngines_RequiresEveryEngine ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await SearchEngineTests.GetIdsAsync (factory);

    HttpResponseMessage partial = await SearchEngineTests.PostAsync (factory: factory,
                                                                     handler: "ReorderSearchEngines",
                                                                     ("orderedIds", ids["Google"].ToString ()));

    await FormPoster.AssertProblemAsync (response: partial,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "The order must include every search engine.");

    HttpResponseMessage full = await SearchEngineTests.PostAsync (factory: factory,
                                                                  handler: "ReorderSearchEngines",
                                                                  ("orderedIds", ids["Google"].ToString ()),
                                                                  ("orderedIds", ids["Bing"].ToString ()),
                                                                  ("orderedIds", ids["DuckDuckGo"].ToString ()));

    Assert.Equal (expected: HttpStatusCode.OK, actual: full.StatusCode);
    Assert.Equal (expected: [("Google", "g", false, 0), ("Bing", "b", true, 1), ("DuckDuckGo", "d", false, 2)],
                  actual: await SearchEngineTests.GetEnginesAsync (factory));
  }

  /// <summary>
  ///   Posts form fields to a Settings handler.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="handler">The handler name.</param>
  /// <param name="fields">The fields.</param>
  /// <returns>The response.</returns>
  private static Task<HttpResponseMessage> PostAsync (DashboardApplicationFactory          factory,
                                                      string                               handler,
                                                      params (string Name, string Value)[] fields)
    => FormPoster.PostAsync (client: factory.CreateClient (),
                             pagePath: SearchEngineTests.SettingsPath,
                             handler: handler,
                             fields: [.. fields.Select (f => new KeyValuePair<string, string> (key: f.Name, value: f.Value))]);

  /// <summary>
  ///   Gets the ID of every engine, by name.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The engine IDs.</returns>
  private static async Task<Dictionary<string, int>> GetIdsAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.SearchEngines.ToDictionaryAsync (keySelector: e => e.Name,
                                                            elementSelector: e => e.Id,
                                                            cancellationToken: TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Gets every engine's name, shortcut, default flag and position, in order.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The engines.</returns>
  private static async Task<List<(string Name, string Shortcut, bool IsDefault, int SortOrder)>> GetEnginesAsync (
    DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      List<SearchEngine> engines = await context.SearchEngines.OrderBy (e => e.SortOrder)
                                                .ToListAsync (TestContext.Current.CancellationToken);

      return [.. engines.Select (e => (e.Name, e.Shortcut, e.IsDefault, e.SortOrder))];
    }
  }
}

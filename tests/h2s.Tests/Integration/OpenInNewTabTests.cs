using System.Net;
using System.Text.Json;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for links that open in a new tab: the tile, the Editor handlers that set it, and Undo.
/// </summary>
public class OpenInNewTabTests
{
  private const string EditorPath = "/Admin/Editor";

  [Fact]
  public async Task Index_OpensOnlyMarkedLinksInANewTab ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    // The seeder marks only GitHub.
    Assert.Single (html.Split ("target=\"_blank\"").Skip (1));
    Assert.Contains (expectedSubstring:
                     "href=\"https://github.com/arkane-systems/h2s\"\n   target=\"_blank\" rel=\"noopener noreferrer\"",
                     actualString: html.Replace (oldValue: "\r\n", newValue: "\n"));
    Assert.Contains (expectedSubstring: "(opens in a new tab)", actualString: html);
  }

  [Fact]
  public async Task CreateThenUpdateLink_SetsAndKeepsTheSetting ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();
    int             media   = await OpenInNewTabTests.GetCategoryIdAsync (factory: factory, name: "Media");

    JsonElement created = await OpenInNewTabTests.PostAsync (client: client,
                                                             handler: "CreateLink",
                                                             ("categoryId", media.ToString ()),
                                                             ("label", "Bazarr"),
                                                             ("url", "https://bazarr.home.arpa/"),
                                                             ("openInNewTab", "true"));
    int id = created.GetProperty ("id").GetInt32 ();

    Assert.True (created.GetProperty ("openInNewTab").GetBoolean ());

    // A post without the field, as the older editor sends, keeps it.
    JsonElement kept = await OpenInNewTabTests.PostAsync (client: client,
                                                          handler: "UpdateLink",
                                                          ("id", id.ToString ()),
                                                          ("categoryId", media.ToString ()),
                                                          ("label", "Bazarr"),
                                                          ("url", "https://bazarr.home.arpa/"));

    Assert.True (kept.GetProperty ("openInNewTab").GetBoolean ());

    JsonElement cleared = await OpenInNewTabTests.PostAsync (client: client,
                                                             handler: "UpdateLink",
                                                             ("id", id.ToString ()),
                                                             ("categoryId", media.ToString ()),
                                                             ("label", "Bazarr"),
                                                             ("url", "https://bazarr.home.arpa/"),
                                                             ("openInNewTab", "false"));

    Assert.False (cleared.GetProperty ("openInNewTab").GetBoolean ());

    using HttpResponseMessage response = await client.GetAsync (requestUri: $"{OpenInNewTabTests.EditorPath}?handler=Link&id={id}",
                                                                cancellationToken: TestContext.Current.CancellationToken);
    Assert.False ((await FormPoster.ReadJsonAsync (response)).GetProperty ("openInNewTab").GetBoolean ());
  }

  [Fact]
  public async Task DeleteThenRestoreLink_KeepsTheSetting ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();
    int             github;

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      github = await context.Links.Where (l => l.Label == "GitHub")
                            .Select (l => l.Id)
                            .SingleAsync (TestContext.Current.CancellationToken);
    }

    JsonElement deleted = await OpenInNewTabTests.PostAsync (client: client, handler: "DeleteLink", ("id", github.ToString ()));

    Assert.True (deleted.GetProperty ("restore").GetProperty ("openInNewTab").GetBoolean ());

    JsonElement restored = await OpenInNewTabTests.PostAsync (client: client,
                                                              handler: "RestoreLink",
                                                              ("snapshot", deleted.GetProperty ("restore").GetRawText ()));

    Assert.True (restored.GetProperty ("openInNewTab").GetBoolean ());
  }

  [Fact]
  public async Task ListView_IncludesTheSetting ()
  {
    await using var factory = new DashboardApplicationFactory ();

    using HttpResponseMessage response = await factory.CreateClient ()
                                                      .GetAsync (requestUri: $"{OpenInNewTabTests.EditorPath}?handler=Links",
                                                                 cancellationToken: TestContext.Current.CancellationToken);
    JsonElement links = await FormPoster.ReadJsonAsync (response);

    Assert.Equal (expected: ["GitHub"],
                  actual: links.EnumerateArray ()
                               .Where (l => l.GetProperty ("openInNewTab").GetBoolean ())
                               .Select (l => l.GetProperty ("label").GetString ()));
  }

  /// <summary>
  ///   Posts form fields to an Editor handler and returns its JSON, asserting success.
  /// </summary>
  /// <param name="client">The client.</param>
  /// <param name="handler">The handler name.</param>
  /// <param name="fields">The fields.</param>
  /// <returns>The JSON.</returns>
  private static async Task<JsonElement> PostAsync (HttpClient client, string handler, params (string Name, string Value)[] fields)
  {
    HttpResponseMessage response = await FormPoster.PostAsync (client: client,
                                                               pagePath: OpenInNewTabTests.EditorPath,
                                                               handler: handler,
                                                               fields:
                                                               [
                                                                 .. fields.Select (f => new
                                                                                     KeyValuePair<string, string> (key: f.Name,
                                                                                                                   value: f.Value)),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return await FormPoster.ReadJsonAsync (response);
  }

  /// <summary>
  ///   Gets a category's ID by name.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="name">The category name.</param>
  /// <returns>The ID.</returns>
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

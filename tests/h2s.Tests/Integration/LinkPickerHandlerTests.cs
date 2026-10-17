using System.Net;
using System.Text.Json;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the link dialog's icon and monitor pickers, and for links' own icon URLs.
/// </summary>
public class LinkPickerHandlerTests
{
  private const string EditorPath = "/Admin/Editor";

  [Fact]
  public async Task SearchIcons_ReturnsMatchesWithThumbnails ()
  {
    await using var factory = new DashboardApplicationFactory ();

    JsonElement icons = await LinkPickerHandlerTests.GetJsonAsync (factory: factory, query: "handler=SearchIcons&q=home");

    JsonElement icon = Assert.Single (icons.EnumerateArray ());

    Assert.Equal (expected: "home-assistant", actual: icon.GetProperty ("reference").GetString ());
    Assert.Equal (expected: "Home Assistant", actual: icon.GetProperty ("name").GetString ());
    Assert.Equal (expected: "https://cdn.jsdelivr.net/gh/selfhst/icons/png/home-assistant.png",
                  actual: icon.GetProperty ("url").GetString ());
  }

  [Fact]
  public async Task KumaMonitors_ExplainsWhenKumaIsNotConfigured ()
  {
    await using var factory = new DashboardApplicationFactory ();

    JsonElement list = await LinkPickerHandlerTests.GetJsonAsync (factory: factory, query: "handler=KumaMonitors");

    Assert.Equal (expected: "Uptime Kuma isn't configured.", actual: list.GetProperty ("message").GetString ());
    Assert.Empty (list.GetProperty ("groups").EnumerateArray ());
  }

  [Fact]
  public async Task KumaMonitors_AsksForAnApiKey ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync ();

    JsonElement list = await LinkPickerHandlerTests.GetJsonAsync (factory: factory, query: "handler=KumaMonitors");

    Assert.Equal (expected: "Add an API key to the Uptime Kuma settings to pick monitors.",
                  actual: list.GetProperty ("message").GetString ());
  }

  [Fact]
  public async Task KumaMonitors_ListsEveryMonitorFromTheMetrics_GroupsFirst ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    JsonElement list = await LinkPickerHandlerTests.GetJsonAsync (factory: factory, query: "handler=KumaMonitors");

    Assert.Equal (expected: JsonValueKind.Null, actual: list.GetProperty ("message").ValueKind);
    Assert.Equal (expected: [("Groups", "1 Infrastructure"), ("Monitors", "11 Jellyfin"), ("Monitors", "12 Sonarr \"TV\"")],
                  actual: list.GetProperty ("groups")
                              .EnumerateArray ()
                              .SelectMany (g => g.GetProperty ("monitors")
                                                 .EnumerateArray ()
                                                 .Select (m => (g.GetProperty ("name").GetString (),
                                                                $"{m.GetProperty ("id").GetInt32 ()} {m.GetProperty ("name").GetString ()}"))));
  }

  [Fact]
  public async Task KumaMonitors_SaysWhenTheApiKeyIsRefused ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: "wrong");

    JsonElement list = await LinkPickerHandlerTests.GetJsonAsync (factory: factory, query: "handler=KumaMonitors");

    Assert.Equal (expected: "Uptime Kuma refused the API key.", actual: list.GetProperty ("message").GetString ());
  }

  [Fact]
  public async Task CreateLink_StoresTheIconUrl ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await LinkPickerHandlerTests.CreateLinkAsync (factory: factory,
                                                                                 iconUrl: " https://wiki.home.arpa/favicon.png ");

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: "https://wiki.home.arpa/favicon.png",
                  actual: (await FormPoster.ReadJsonAsync (response)).GetProperty ("iconUrl").GetString ());
  }

  [Theory]
  [InlineData ("not a url")]
  [InlineData ("ftp://wiki.home.arpa/favicon.png")]
  [InlineData ("/favicon.png")]
  public async Task CreateLink_RejectsAnInvalidIconUrl (string iconUrl)
  {
    await using var factory = new DashboardApplicationFactory ();

    await FormPoster.AssertProblemAsync (response:
                                         await LinkPickerHandlerTests.CreateLinkAsync (factory: factory, iconUrl: iconUrl),
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Icon URL must be a valid HTTP or HTTPS URL.");
  }

  [Theory]
  [InlineData (null,                              "https://hub.docker.com/favicon.ico")]
  [InlineData ("",                                "")]
  [InlineData ("https://hub.docker.com/icon.png", "https://hub.docker.com/icon.png")]
  public async Task UpdateLink_ChangesTheIconUrlOnlyWhenPosted (string? iconUrl, string expected)
  {
    await using var factory = new DashboardApplicationFactory ();
    int             linkId  = await LinkPickerHandlerTests.GetLinkIdAsync (factory: factory, label: "Docker Hub");

    List<KeyValuePair<string, string>> fields =
    [
      new (key: "id", value: linkId.ToString ()),
      new (key: "categoryId",
           value: (await LinkPickerHandlerTests.GetCategoryIdAsync (factory: factory, name: "Reference")).ToString ()),
      new (key: "label", value: "Docker Hub"),
      new (key: "url", value: "https://hub.docker.com/"),
    ];

    if (iconUrl != null)
    {
      fields.Add (new KeyValuePair<string, string> (key: "iconUrl", value: iconUrl));
    }

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: LinkPickerHandlerTests.EditorPath,
                                                               handler: "UpdateLink",
                                                               fields: fields);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: expected, actual: (await FormPoster.ReadJsonAsync (response)).GetProperty ("iconUrl").GetString ());
  }

  [Fact]
  public async Task DeleteThenRestoreLink_KeepsTheIconUrl ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();
    int             linkId  = await LinkPickerHandlerTests.GetLinkIdAsync (factory: factory, label: "Docker Hub");

    HttpResponseMessage deleted = await FormPoster.PostAsync (client: client,
                                                              pagePath: LinkPickerHandlerTests.EditorPath,
                                                              handler: "DeleteLink",
                                                              fields:
                                                              [
                                                                new KeyValuePair<string, string> (key: "id",
                                                                                                  value: linkId.ToString ()),
                                                              ]);
    JsonElement restore = (await FormPoster.ReadJsonAsync (deleted)).GetProperty ("restore");

    HttpResponseMessage restored = await FormPoster.PostAsync (client: client,
                                                               pagePath: LinkPickerHandlerTests.EditorPath,
                                                               handler: "RestoreLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "snapshot",
                                                                                                   value: restore.GetRawText ()),
                                                               ]);

    Assert.Equal (expected: "https://hub.docker.com/favicon.ico",
                  actual: (await FormPoster.ReadJsonAsync (restored)).GetProperty ("iconUrl").GetString ());
  }

  [Fact]
  public async Task Index_ShowsTheIconUrlWhenThereIsNoIconName ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring:
                     "<img class=\"link-block-icon\" src=\"https://hub.docker.com/favicon.ico\" alt=\"\"",
                     actualString: html);
  }

  [Fact]
  public async Task Index_EditModeOffersThePickers ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await LinkPickerHandlerTests.ConfigureKumaAsync (factory: factory, slug: "main");

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/?edit=true",
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """data-search-icons-url="/Admin/Editor?handler=SearchIcons""", actualString: html);
    Assert.Contains (expectedSubstring: """data-choices-url="/Admin/Editor?handler=Choices""",          actualString: html);
    Assert.DoesNotContain (expectedSubstring: """data-role="monitor-select""", actualString: html);
    Assert.Contains (expectedSubstring: """name="iconUrl""", actualString: html);
  }

  /// <summary>
  ///   Configures Uptime Kuma in the settings, before the first request so the settings cache picks it up.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="slug">The status page slug, or <c>null</c> for none.</param>
  private static Task ConfigureKumaAsync (DashboardApplicationFactory factory, string? slug)
    => factory.AddUptimeKumaAsync (statusPageSlug: slug ?? "");

  /// <summary>
  ///   Creates a link in Media with the given icon URL.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="iconUrl">The icon URL.</param>
  /// <returns>The response.</returns>
  private static async Task<HttpResponseMessage> CreateLinkAsync (DashboardApplicationFactory factory, string iconUrl)
    => await FormPoster.PostAsync (client: factory.CreateClient (),
                                   pagePath: LinkPickerHandlerTests.EditorPath,
                                   handler: "CreateLink",
                                   fields:
                                   [
                                     new KeyValuePair<string, string> (key: "categoryId",
                                                                       value:
                                                                       (await LinkPickerHandlerTests
                                                                         .GetCategoryIdAsync (factory: factory, name: "Media"))
                                                                      .ToString ()),
                                     new KeyValuePair<string, string> (key: "label",   value: "Wiki"),
                                     new KeyValuePair<string, string> (key: "url",     value: "https://wiki.home.arpa/"),
                                     new KeyValuePair<string, string> (key: "iconUrl", value: iconUrl),
                                   ]);

  /// <summary>
  ///   Gets JSON from an editor handler, asserting success.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="query">The query string, including the handler.</param>
  /// <returns>The JSON.</returns>
  private static async Task<JsonElement> GetJsonAsync (DashboardApplicationFactory factory, string query)
  {
    HttpResponseMessage response = await factory.CreateClient ()
                                                .GetAsync (requestUri: $"{LinkPickerHandlerTests.EditorPath}?{query}",
                                                           cancellationToken: TestContext.Current.CancellationToken);

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
}

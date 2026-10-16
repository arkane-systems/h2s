using System.Net;
using System.Text;
using System.Text.Json;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the settings page and the color mode toggle.
/// </summary>
public class SettingsPageTests
{
  private const string SettingsPath = "/Admin/Settings";

  [Fact]
  public async Task CreateInfrastructureGroup_ReturnsNumericMonitorId ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: SettingsPageTests.SettingsPath,
                                                               handler: "CreateInfrastructureGroup",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "name",      value: "Edge"),
                                                                 new KeyValuePair<string, string> (key: "monitorId", value: " 35 "),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: 35, actual: (await FormPoster.ReadJsonAsync (response)).GetProperty ("monitorId").GetInt32 ());
  }

  [Theory]
  [InlineData ("")]
  [InlineData ("0")]
  [InlineData ("abc")]
  public async Task CreateInfrastructureGroup_RejectsInvalidMonitorId (string monitorId)
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: SettingsPageTests.SettingsPath,
                                                               handler: "CreateInfrastructureGroup",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "name", value: "Edge"),
                                                                 new KeyValuePair<string, string> (key: "monitorId",
                                                                                                   value: monitorId),
                                                               ]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Monitor ID must be a positive integer.");
  }

  [Fact]
  public async Task Get_ShowsTheSectionsWithoutJQuery ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: SettingsPageTests.SettingsPath,
                                                cancellationToken: TestContext.Current.CancellationToken);

    foreach (string section in new[] { "general", "appearance", "integrations", "uptime-kuma", "backup" })
      Assert.Contains (expectedSubstring: $"""<section id="{section}" """, actualString: html);

    Assert.Contains (expectedSubstring: """value="Development""",              actualString: html);
    Assert.Contains (expectedSubstring: """id="infrastructure-group-dialog""", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "jquery", actualString: html, comparisonType: StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task SaveGeneral_SavesTheValues ()
  {
    await using var factory = new DashboardApplicationFactory ();

    JsonElement saved = await SettingsPageTests.PostOkAsync (factory: factory,
                                                             handler: "SaveGeneral",
                                                             fields:
                                                             [
                                                               new KeyValuePair<string, string> (key: "title", value: "  Homelab "),
                                                               new KeyValuePair<string, string> (key: "motto",
                                                                                                 value: "Everything in one place"),
                                                               new KeyValuePair<string, string> (key: "localDomains",
                                                                                                 value: "home.arpa, lab.example"),
                                                             ]);

    Assert.Equal (expected: "Homelab", actual: saved.GetProperty ("title").GetString ());

    DashboardSettings settings = await SettingsPageTests.ReadSettingsAsync (factory);

    Assert.Equal (expected: "Homelab",                 actual: settings.Title);
    Assert.Equal (expected: "Everything in one place", actual: settings.Motto);
    Assert.Equal (expected: "home.arpa, lab.example",  actual: settings.LocalDomains);
  }

  [Fact]
  public async Task SaveGeneral_AllowsClearingOptionalFields ()
  {
    await using var factory = new DashboardApplicationFactory ();

    _ = await SettingsPageTests.PostOkAsync (factory: factory,
                                             handler: "SaveGeneral",
                                             fields:
                                             [
                                               new KeyValuePair<string, string> (key: "title",        value: "Homelab"),
                                               new KeyValuePair<string, string> (key: "motto",        value: ""),
                                               new KeyValuePair<string, string> (key: "localDomains", value: ""),
                                             ]);

    DashboardSettings settings = await SettingsPageTests.ReadSettingsAsync (factory);

    Assert.Equal (expected: "", actual: settings.Motto);
    Assert.Equal (expected: "", actual: settings.LocalDomains);
  }

  [Fact]
  public async Task SaveGeneral_RequiresTitle ()
  {
    await using var factory = new DashboardApplicationFactory ();

    await FormPoster.AssertProblemAsync (response: await FormPoster.PostAsync (client: factory.CreateClient (),
                                                                               pagePath: SettingsPageTests.SettingsPath,
                                                                               handler: "SaveGeneral",
                                                                               fields:
                                                                               [
                                                                                 new KeyValuePair<string, string> (key: "title",
                                                                                                                   value: "  "),
                                                                               ]),
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Title is required.");
  }

  [Fact]
  public async Task SaveGeneral_UpdatesTheRenderedDashboard ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    // Render once first, so the settings are cached before they change.
    _ = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);
    _ = await FormPoster.PostAsync (client: client,
                                    pagePath: SettingsPageTests.SettingsPath,
                                    handler: "SaveGeneral",
                                    fields: [new KeyValuePair<string, string> (key: "title", value: "Renamed Lab")]);

    string html = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "<title>h2s - Renamed Lab</title>", actualString: html);
  }

  [Theory]
  [InlineData ("Dark",  ColorMode.Dark)]
  [InlineData ("light", ColorMode.Light)]
  [InlineData ("0",     ColorMode.Auto)]
  public async Task SaveAppearance_SavesTheColorMode (string value, ColorMode expected)
  {
    await using var factory = new DashboardApplicationFactory ();

    _ = await SettingsPageTests.PostOkAsync (factory: factory,
                                             handler: "SaveAppearance",
                                             fields: [new KeyValuePair<string, string> (key: "colorMode", value: value)]);

    Assert.Equal (expected: expected, actual: (await SettingsPageTests.ReadSettingsAsync (factory)).ColorMode);
  }

  [Theory]
  [InlineData ("Sepia")]
  [InlineData ("7")]
  [InlineData ("")]
  public async Task SaveAppearance_RejectsUnknownModes (string value)
  {
    await using var factory = new DashboardApplicationFactory ();

    await FormPoster.AssertProblemAsync (response: await FormPoster.PostAsync (client: factory.CreateClient (),
                                                                               pagePath: SettingsPageTests.SettingsPath,
                                                                               handler: "SaveAppearance",
                                                                               fields:
                                                                               [
                                                                                 new KeyValuePair<string, string> (key: "colorMode",
                                                                                                                   value: value),
                                                                               ]),
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Color mode must be Auto, Light or Dark.");
  }

  [Fact]
  public async Task SaveUptimeKuma_SavesAndTurnsTheIntegrationOnAndOff ()
  {
    await using var factory = new DashboardApplicationFactory ();

    JsonElement on = await SettingsPageTests.PostOkAsync (factory: factory,
                                                          handler: "SaveUptimeKuma",
                                                          fields:
                                                          [
                                                            new KeyValuePair<string, string> (key: "serverUrl",
                                                                                              value: " https://status.home.arpa "),
                                                            new KeyValuePair<string, string> (key: "statusPageSlug", value: "main"),
                                                            new KeyValuePair<string, string> (key: "defaultDuration", value: "48"),
                                                          ]);

    Assert.True (on.GetProperty ("isConfigured").GetBoolean ());

    Assert.Equal (expected: new UptimeKumaSettings (ServerUrl: "https://status.home.arpa",
                                                    StatusPageSlug: "main",
                                                    DefaultDuration: 48),
                  actual: await TestConnections.ReadUptimeKumaAsync (factory));

    JsonElement off = await SettingsPageTests.PostOkAsync (factory: factory,
                                                           handler: "SaveUptimeKuma",
                                                           fields:
                                                           [
                                                             new KeyValuePair<string, string> (key: "serverUrl",       value: ""),
                                                             new KeyValuePair<string, string> (key: "statusPageSlug",  value: ""),
                                                             new KeyValuePair<string, string> (key: "defaultDuration", value: "24"),
                                                           ]);

    Assert.False (off.GetProperty ("isConfigured").GetBoolean ());
    Assert.Null (await TestConnections.ReadUptimeKumaAsync (factory));
  }

  [Theory]
  [InlineData ("https://status.home.arpa/dashboard",
               "main",
               "24",
               "Uptime Kuma server URL must be a valid HTTP or HTTPS base URL with no path, query, or fragment.")]
  [InlineData ("https://status.home.arpa", "not a slug!", "24", "Status page slug may only contain letters, numbers, and hyphens.")]
  [InlineData ("ftp://status.home.arpa",   "main",        "24", "Server URL must be an HTTP or HTTPS URL.")]
  [InlineData ("https://status.home.arpa", "main",        "0", "Default uptime duration (hours) must be at least 1.")]
  [InlineData ("https://status.home.arpa", "main",        "1.5", "Default uptime duration (hours) must be a whole number.")]
  [InlineData ("https://status.home.arpa", "main",        "", "Default uptime duration (hours) is required.")]
  public async Task SaveUptimeKuma_RejectsInvalidValues (string serverUrl, string slug, string duration, string detail)
  {
    await using var factory = new DashboardApplicationFactory ();

    await FormPoster.AssertProblemAsync (response: await FormPoster.PostAsync (client: factory.CreateClient (),
                                                                               pagePath: SettingsPageTests.SettingsPath,
                                                                               handler: "SaveUptimeKuma",
                                                                               fields:
                                                                               [
                                                                                 new KeyValuePair<string, string> (key: "serverUrl",
                                                                                                                   value:
                                                                                                                   serverUrl),
                                                                                 new KeyValuePair<string,
                                                                                   string> (key: "statusPageSlug", value: slug),
                                                                                 new KeyValuePair<string,
                                                                                   string> (key: "defaultDuration",
                                                                                            value: duration),
                                                                               ]),
                                         status: HttpStatusCode.BadRequest,
                                         detail: detail);
  }

  [Fact]
  public async Task TestUptimeKuma_ReportsTheStatusPagesMonitors ()
  {
    await using var factory = new DashboardApplicationFactory ();
    factory.Outbound = request => request.RequestUri!.AbsolutePath switch
                                  {
                                    "/" => new HttpResponseMessage (HttpStatusCode.OK),
                                    "/api/status-page/main" => new HttpResponseMessage (HttpStatusCode.OK)
                                                               {
                                                                 Content = new StringContent (content: """
                                                                                                       { "publicGroupList": [
                                                                                                         { "name": "Core", "monitorList": [ { "id": 1, "name": "A" }, { "id": 2, "name": "B" } ] }
                                                                                                       ] }
                                                                                                       """,
                                                                                              encoding: Encoding.UTF8,
                                                                                              mediaType: "application/json"),
                                                               },
                                    _ => new HttpResponseMessage (HttpStatusCode.NotFound),
                                  };

    JsonElement result = await SettingsPageTests.PostOkAsync (factory: factory,
                                                              handler: "TestUptimeKuma",
                                                              fields:
                                                              [
                                                                new KeyValuePair<string, string> (key: "serverUrl",
                                                                                                  value:
                                                                                                  "https://status.home.arpa"),
                                                                new KeyValuePair<string, string> (key: "statusPageSlug",
                                                                                                  value: "main"),
                                                              ]);

    Assert.Equal (expected: "success", actual: result.GetProperty ("level").GetString ());
    Assert.Equal (expected: "Connected. The status page lists 2 monitors in 1 group.",
                  actual: result.GetProperty ("message").GetString ());

    // Testing doesn't save anything.
    Assert.Null (await TestConnections.ReadUptimeKumaAsync (factory));
  }

  [Fact]
  public async Task TestUptimeKuma_ReportsAnUnreachableServer ()
  {
    await using var factory = new DashboardApplicationFactory ();
    factory.Outbound = _ => throw new HttpRequestException ("Connection refused");

    JsonElement result = await SettingsPageTests.PostOkAsync (factory: factory,
                                                              handler: "TestUptimeKuma",
                                                              fields:
                                                              [
                                                                new KeyValuePair<string, string> (key: "serverUrl",
                                                                                                  value:
                                                                                                  "https://status.home.arpa"),
                                                                new KeyValuePair<string, string> (key: "statusPageSlug", value: ""),
                                                              ]);

    Assert.Equal (expected: "danger", actual: result.GetProperty ("level").GetString ());
    Assert.Equal (expected: "Couldn't reach Uptime Kuma at https://status.home.arpa.",
                  actual: result.GetProperty ("message").GetString ());
  }

  [Fact]
  public async Task TestUptimeKuma_RequiresAServerUrl ()
  {
    await using var factory = new DashboardApplicationFactory ();

    await FormPoster.AssertProblemAsync (response: await FormPoster.PostAsync (client: factory.CreateClient (),
                                                                               pagePath: SettingsPageTests.SettingsPath,
                                                                               handler: "TestUptimeKuma",
                                                                               fields:
                                                                               [
                                                                                 new KeyValuePair<string, string> (key: "serverUrl",
                                                                                                                   value: " "),
                                                                               ]),
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Enter the Uptime Kuma server URL to test.");
  }

  [Fact]
  public async Task DeleteThenRestoreInfrastructureGroup_PutsItBack ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();
    int             id;

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      id = await context.InfrastructureGroups.Where (g => g.Name == "Storage")
                        .Select (g => g.Id)
                        .SingleAsync (TestContext.Current.CancellationToken);
    }

    JsonElement deleted = await SettingsPageTests.PostOkAsync (factory: factory,
                                                               handler: "DeleteInfrastructureGroup",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "id",
                                                                                                   value: id.ToString ()),
                                                               ],
                                                               client: client);
    JsonElement restore = deleted.GetProperty ("restore");

    _ = await SettingsPageTests.PostOkAsync (factory: factory,
                                             handler: "RestoreInfrastructureGroup",
                                             fields:
                                             [
                                               new KeyValuePair<string, string> (key: "name",
                                                                                 value: restore.GetProperty ("name").GetString ()!),
                                               new KeyValuePair<string, string> (key: "monitorId",
                                                                                 value: restore.GetProperty ("monitorId")
                                                                                               .GetInt32 ()
                                                                                               .ToString ()),
                                               new KeyValuePair<string, string> (key: "sortOrder",
                                                                                 value: restore.GetProperty ("sortOrder")
                                                                                               .GetInt32 ()
                                                                                               .ToString ()),
                                             ],
                                             client: client);

    (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Assert.Equal (expected: ["Compute", "Storage", "Network"],
                    actual: await context.InfrastructureGroups.OrderBy (g => g.SortOrder)
                                         .Select (g => g.Name)
                                         .ToListAsync (TestContext.Current.CancellationToken));
    }
  }

  [Fact]
  public async Task ColorMode_FollowsTheSiteDefaultWithoutACookie ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      await context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression:
                                                                                                x => x.ColorMode,
                                                                                                valueExpression: ColorMode.Dark),
                                                          cancellationToken: TestContext.Current.CancellationToken);
    }

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: $"const mode = {(int)ColorMode.Dark};",                  actualString: html);
    Assert.Contains (expectedSubstring: "data-viewer-mode=\"\"",                                 actualString: html);
    Assert.Contains (expectedSubstring: "data-site-mode=\"Dark\"",                               actualString: html);
    Assert.Contains (expectedSubstring: "Site default (Dark). Click for light in this browser.", actualString: html);
  }

  [Theory]
  [InlineData ("light", ColorMode.Light, "Light")]
  [InlineData ("DARK",  ColorMode.Dark,  "Dark")]
  [InlineData ("auto",  ColorMode.Auto,  "Auto")]
  [InlineData ("pink",  ColorMode.Auto,  "")]
  public async Task ColorMode_ThisBrowsersCookieWins (string cookie, ColorMode mode, string viewerMode)
  {
    await using var factory = new DashboardApplicationFactory ();
    using var       request = new HttpRequestMessage (method: HttpMethod.Get, requestUri: "/");
    request.Headers.Add (name: "Cookie", value: $"h2s-color-mode={cookie}");

    using HttpResponseMessage response = await factory.CreateClient ()
                                                      .SendAsync (request: request,
                                                                  cancellationToken: TestContext.Current.CancellationToken);
    string html = await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);

    // The seeded site default is Auto, so an unreadable cookie falls back to it.
    Assert.Contains (expectedSubstring: $"const mode = {(int)mode};",         actualString: html);
    Assert.Contains (expectedSubstring: $"data-viewer-mode=\"{viewerMode}\"", actualString: html);
  }

  [Fact]
  public async Task ToggleColorMode_IsGone ()
  {
    await using var factory = new DashboardApplicationFactory ();

    using HttpResponseMessage response = await factory.CreateClient ()
                                                      .GetAsync (requestUri: "/Admin/ToggleColorMode",
                                                                 cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.NotFound, actual: response.StatusCode);
  }

  /// <summary>
  ///   Posts to a Settings handler and returns its JSON, asserting success.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="handler">The handler name.</param>
  /// <param name="fields">The form fields.</param>
  /// <param name="client">The client to use, or <c>null</c> for a new one.</param>
  /// <returns>The JSON.</returns>
  private static async Task<JsonElement> PostOkAsync (DashboardApplicationFactory    factory,
                                                      string                         handler,
                                                      KeyValuePair<string, string>[] fields,
                                                      HttpClient?                    client = null)
  {
    HttpResponseMessage response = await FormPoster.PostAsync (client: client ?? factory.CreateClient (),
                                                               pagePath: SettingsPageTests.SettingsPath,
                                                               handler: handler,
                                                               fields: fields);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return await FormPoster.ReadJsonAsync (response);
  }

  /// <summary>
  ///   Reads the stored settings.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The settings.</returns>
  private static async Task<DashboardSettings> ReadSettingsAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
      return await context.DashboardSettings.AsNoTracking ().SingleAsync (TestContext.Current.CancellationToken);
  }
}

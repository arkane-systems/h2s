using System.Net;
using h2s.Models;
using h2s.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
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
  public async Task Post_RedisplaysPageForInvalidInput ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: SettingsPageTests.SettingsPath,
                                                               handler: null,
                                                               fields: SettingsPageTests.Form (title: "Homelab",
                                                                                               motto: "Lab",
                                                                                               slug: "not a slug!"));

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Contains (expectedSubstring: "Status page slug may only contain letters, numbers, and hyphens.",
                     actualString: await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Post_RequiresTitle ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: SettingsPageTests.SettingsPath,
                                                               handler: null,
                                                               fields: SettingsPageTests.Form (title: "", motto: "", slug: ""));

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Contains (expectedSubstring: "The Title field is required.",
                     actualString: await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Post_SavesValidSettings ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient (new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    HttpResponseMessage response = await FormPoster.PostAsync (client: client,
                                                               pagePath: SettingsPageTests.SettingsPath,
                                                               handler: null,
                                                               fields: SettingsPageTests.Form (title: "Homelab",
                                                                                               motto: "Everything in one place",
                                                                                               slug: ""));

    Assert.Equal (expected: HttpStatusCode.Redirect, actual: response.StatusCode);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      DashboardSettings settings = await context.DashboardSettings.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal (expected: "Homelab",                 actual: settings.Title);
      Assert.Equal (expected: "Everything in one place", actual: settings.Motto);
      Assert.Equal (expected: ColorMode.Dark,            actual: settings.ColorMode);
    }
  }

  [Fact]
  public async Task Post_AllowsClearingOptionalFields ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient (new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    IEnumerable<KeyValuePair<string, string>> fields = SettingsPageTests.Form (title: "Homelab", motto: "", slug: "")
                                                                        .Select (f => f.Key == "Settings.LocalDomains"
                                                                                        ? new KeyValuePair<string,
                                                                                          string> (key: f.Key, value: "")
                                                                                        : f);

    HttpResponseMessage response =
      await FormPoster.PostAsync (client: client, pagePath: SettingsPageTests.SettingsPath, handler: null, fields: fields);

    Assert.Equal (expected: HttpStatusCode.Redirect, actual: response.StatusCode);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      DashboardSettings settings = await context.DashboardSettings.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal (expected: "", actual: settings.Motto);
      Assert.Equal (expected: "", actual: settings.LocalDomains);
    }
  }

  [Fact]
  public async Task Get_ShowsCurrentSettings ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: SettingsPageTests.SettingsPath,
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """value="Development""", actualString: html);
  }

  [Fact]
  public async Task Post_UpdatesTheRenderedDashboard ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    // Render once first, so the settings are cached before they change.
    _ = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);
    _ = await FormPoster.PostAsync (client: client,
                                    pagePath: SettingsPageTests.SettingsPath,
                                    handler: null,
                                    fields: SettingsPageTests.Form (title: "Renamed Lab", motto: "", slug: ""));

    string html = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "<title>h2s - Renamed Lab</title>", actualString: html);
  }

  [Fact]
  public async Task ToggleColorMode_UpdatesTheRenderedDashboard ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    _ = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);
    _ = await FormPoster.PostAsync (client: client, pagePath: "/Admin/ToggleColorMode", handler: null, fields: []);

    string html = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: $"data-color-mode=\"{(int)ColorMode.Light}\"", actualString: html);
  }

  [Fact]
  public async Task ToggleColorMode_CyclesThroughModes ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();
    var             modes   = new List<string?> ();

    for (var i = 0; i < 3; i++)
    {
      HttpResponseMessage response =
        await FormPoster.PostAsync (client: client, pagePath: "/Admin/ToggleColorMode", handler: null, fields: []);
      modes.Add ((await FormPoster.ReadJsonAsync (response)).GetProperty ("colorMode").GetString ());
    }

    Assert.Equal (expected: ["Light", "Dark", "Auto"], actual: modes);
  }

  /// <summary>
  ///   Builds the settings form fields. Uptime Kuma stays disabled so the post does not probe the network.
  /// </summary>
  /// <param name="title">The dashboard title.</param>
  /// <param name="motto">The motto.</param>
  /// <param name="slug">The Uptime Kuma status page slug.</param>
  /// <returns>The form fields.</returns>
  private static KeyValuePair<string, string>[] Form (string title, string motto, string slug)
    =>
    [
      new (key: "Settings.Id", value: "1"),
      new (key: "Settings.Title", value: title),
      new (key: "Settings.Motto", value: motto),
      new (key: "Settings.LocalDomains", value: "home.arpa"),
      new (key: "Settings.ColorMode", value: ((int)ColorMode.Dark).ToString ()),
      new (key: "Settings.UptimeKumaServerUrl", value: ""),
      new (key: "Settings.UptimeKumaStatusPageSlug", value: slug),
      new (key: "Settings.UptimeKumaDefaultDuration", value: "24"),
    ];
}

using System.Net;
using h2s.Models;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
/// Tests for the settings page and the color mode toggle.
/// </summary>
public class SettingsPageTests
{
  private const string SettingsPath = "/Admin/Settings";

  [Fact]
  public async Task Post_RedisplaysPageForInvalidInput ()
  {
    await using var factory = new DashboardApplicationFactory ();

    var response = await FormPoster.PostAsync (factory.CreateClient (),
                                               SettingsPageTests.SettingsPath,
                                               handler: null,
                                               SettingsPageTests.Form (title: "Homelab", motto: "Lab", slug: "not a slug!"));

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);
    Assert.Contains ("Status page slug may only contain letters, numbers, and hyphens.",
                     await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Post_RequiresTitle ()
  {
    await using var factory = new DashboardApplicationFactory ();

    var response = await FormPoster.PostAsync (factory.CreateClient (),
                                               SettingsPageTests.SettingsPath,
                                               handler: null,
                                               SettingsPageTests.Form (title: "", motto: "", slug: ""));

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);
    Assert.Contains ("The Title field is required.", await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Post_SavesValidSettings ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var client              = factory.CreateClient (new () { AllowAutoRedirect = false });

    var response = await FormPoster.PostAsync (client,
                                               SettingsPageTests.SettingsPath,
                                               handler: null,
                                               SettingsPageTests.Form (title: "Homelab", motto: "Everything in one place", slug: ""));

    Assert.Equal (HttpStatusCode.Redirect, response.StatusCode);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      var settings = await context.DashboardSettings.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal ("Homelab", settings.Title);
      Assert.Equal ("Everything in one place", settings.Motto);
      Assert.Equal (ColorMode.Dark, settings.ColorMode);
    }
  }

  [Fact]
  public async Task Post_AllowsClearingOptionalFields ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var client              = factory.CreateClient (new () { AllowAutoRedirect = false });
    var fields              = SettingsPageTests.Form (title: "Homelab", motto: "", slug: "")
                                               .Select (f => f.Key == "Settings.LocalDomains" ? new (f.Key, "") : f);

    var response = await FormPoster.PostAsync (client, SettingsPageTests.SettingsPath, handler: null, fields);

    Assert.Equal (HttpStatusCode.Redirect, response.StatusCode);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      var settings = await context.DashboardSettings.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal ("", settings.Motto);
      Assert.Equal ("", settings.LocalDomains);
    }
  }

  [Fact]
  public async Task Get_ShowsCurrentSettings ()
  {
    await using var factory = new DashboardApplicationFactory ();

    var html = await factory.CreateClient ().GetStringAsync (SettingsPageTests.SettingsPath, TestContext.Current.CancellationToken);

    Assert.Contains ("""value="Development""", html);
  }

  [Fact]
  public async Task ToggleColorMode_CyclesThroughModes ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var client              = factory.CreateClient ();
    var modes               = new List<string?> ();

    for (var i = 0; i < 3; i++)
    {
      var response = await FormPoster.PostAsync (client, "/Admin/ToggleColorMode", handler: null, []);
      modes.Add ((await FormPoster.ReadJsonAsync (response)).GetProperty ("colorMode").GetString ());
    }

    Assert.Equal (["Light", "Dark", "Auto"], modes);
  }

  /// <summary>
  /// Builds the settings form fields. Uptime Kuma stays disabled so the post does not probe the network.
  /// </summary>
  /// <param name="title">The dashboard title.</param>
  /// <param name="motto">The motto.</param>
  /// <param name="slug">The Uptime Kuma status page slug.</param>
  /// <returns>The form fields.</returns>
  private static KeyValuePair<string, string>[] Form (string title, string motto, string slug)
    =>
    [
      new ("Settings.Id", "1"),
      new ("Settings.Title", title),
      new ("Settings.Motto", motto),
      new ("Settings.LocalDomains", "home.arpa"),
      new ("Settings.ColorMode", ((int) ColorMode.Dark).ToString ()),
      new ("Settings.UptimeKumaServerUrl", ""),
      new ("Settings.UptimeKumaStatusPageSlug", slug),
      new ("Settings.UptimeKumaDefaultDuration", "24"),
    ];
}

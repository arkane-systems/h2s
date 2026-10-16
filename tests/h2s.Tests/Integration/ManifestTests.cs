using System.Net;
using System.Text.Json;
using h2s.Models;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for installing h2s as an app: the web app manifest, its icons, and the layout's links to them.
/// </summary>
public class ManifestTests
{
  [Fact]
  public async Task Manifest_IsNamedFromSettings ()
  {
    await using var factory = new DashboardApplicationFactory ();

    using HttpResponseMessage response = await factory.CreateClient ()
                                                      .GetAsync (requestUri: "/manifest.webmanifest",
                                                                 cancellationToken: TestContext.Current.CancellationToken);
    JsonElement manifest = await FormPoster.ReadJsonAsync (response);

    Assert.Equal (expected: HttpStatusCode.OK,           actual: response.StatusCode);
    Assert.Equal (expected: "application/manifest+json", actual: response.Content.Headers.ContentType?.MediaType);

    // The seeder's title and motto.
    Assert.Equal (expected: "h2s - Development",  actual: manifest.GetProperty ("name").GetString ());
    Assert.Equal (expected: "Development",        actual: manifest.GetProperty ("short_name").GetString ());
    Assert.Equal (expected: "Seeded sample data", actual: manifest.GetProperty ("description").GetString ());
    Assert.Equal (expected: "/",                  actual: manifest.GetProperty ("start_url").GetString ());
    Assert.Equal (expected: "standalone",         actual: manifest.GetProperty ("display").GetString ());
    Assert.Equal (expected: ["192x192 any", "512x512 any", "512x512 maskable"],
                  actual: manifest.GetProperty ("icons")
                                  .EnumerateArray ()
                                  .Select (i => $"{i.GetProperty ("sizes").GetString ()} {i.GetProperty ("purpose").GetString ()}"));
  }

  [Theory]
  [InlineData ("/icons/icon-192.png")]
  [InlineData ("/icons/icon-512.png")]
  [InlineData ("/icons/icon-maskable-512.png")]
  [InlineData ("/icons/apple-touch-icon.png")]
  public async Task Icons_AreServed (string path)
  {
    await using var factory = new DashboardApplicationFactory ();

    using HttpResponseMessage response = await factory.CreateClient ()
                                                      .GetAsync (requestUri: path,
                                                                 cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: "image/png",       actual: response.Content.Headers.ContentType?.MediaType);
  }

  [Theory]
  [InlineData (ColorMode.Auto, new[] { """<meta name="theme-color" content="#f8f9fa" media="(prefers-color-scheme: light)" />""" })]
  [InlineData (ColorMode.Dark, new[] { """<meta name="theme-color" content="#2b3035" />""" })]
  [InlineData (ColorMode.Light, new[] { """<meta name="theme-color" content="#f8f9fa" />""" })]
  public async Task Layout_LinksTheManifestAndFollowsTheColorMode (ColorMode mode, string[] themeColors)
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      await context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression:
                                                                                                x => x.ColorMode,
                                                                                                valueExpression: mode),
                                                          cancellationToken: TestContext.Current.CancellationToken);
    }

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """<link rel="manifest" href="/manifest.webmanifest" />""", actualString: html);

    // Static assets are fingerprinted.
    Assert.Matches (expectedRegexPattern: """<link rel="apple-touch-icon" href="/icons/apple-touch-icon\.[a-z0-9]+\.png" />""",
                    actualString: html);
    Assert.All (collection: themeColors, action: tag => Assert.Contains (expectedSubstring: tag, actualString: html));
  }
}

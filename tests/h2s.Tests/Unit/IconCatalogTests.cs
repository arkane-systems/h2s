using System.Net;
using System.Text;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="IconCatalog" />.
/// </summary>
public class IconCatalogTests
{
  [Theory]
  [InlineData ("Jellyfin",       "jellyfin")]
  [InlineData ("JELLYFIN",       "jellyfin")]
  [InlineData ("Home Assistant", "home-assistant")]
  [InlineData ("homeassistant",  "home-assistant")]
  [InlineData ("home-assistant", "home-assistant")]
  [InlineData ("Unknown App",    null)]
  public async Task SuggestIconNameAsync_MatchesIconNamesAndReferences (string label, string? expected)
    => Assert.Equal (expected: expected,
                     actual: await IconCatalogTests.CreateCatalog (IconCatalogTests.ServeIndex ())
                                                   .SuggestIconNameAsync (label: label,
                                                                          cancellationToken: TestContext.Current
                                                                                                        .CancellationToken));

  [Fact]
  public async Task SuggestIconNameAsync_TriesCandidatesInOrder ()
    => Assert.Equal (expected: "github",
                     actual: await IconCatalogTests.CreateCatalog (IconCatalogTests.ServeIndex ())
                                                   .SuggestIconNameAsync (candidates:
                                                                          [
                                                                            null, "   ", "Source code", "GitHub", "Jellyfin",
                                                                          ],
                                                                          cancellationToken: TestContext.Current
                                                                                                        .CancellationToken));

  [Fact]
  public async Task SuggestIconNameAsync_DownloadsTheIndexOnce ()
  {
    StubHttpMessageHandler handler = IconCatalogTests.ServeIndex ();
    IconCatalog            catalog = IconCatalogTests.CreateCatalog (handler);

    _ = await catalog.SuggestIconNameAsync (label: "Jellyfin",       cancellationToken: TestContext.Current.CancellationToken);
    _ = await catalog.SuggestIconNameAsync (label: "Home Assistant", cancellationToken: TestContext.Current.CancellationToken);

    HttpRequestMessage request = Assert.Single (handler.Requests);

    Assert.Equal (expected: new Uri (IconCatalog.IndexUrl), actual: request.RequestUri);
  }

  [Theory]
  [InlineData (HttpStatusCode.InternalServerError, "[]")]
  [InlineData (HttpStatusCode.OK,                  "not json")]
  public async Task SuggestIconNameAsync_ReturnsNullWhenTheIndexIsUnavailable (HttpStatusCode status, string body)
  {
    var handler = new StubHttpMessageHandler (_ => new HttpResponseMessage (status) { Content = new StringContent (body) });

    Assert.Null (await IconCatalogTests.CreateCatalog (handler)
                                       .SuggestIconNameAsync (label: "Jellyfin",
                                                              cancellationToken: TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task SuggestIconNameAsync_DoesNotCacheCancelledDownloads ()
  {
    IconCatalog catalog = IconCatalogTests.CreateCatalog (IconCatalogTests.ServeIndex ());

    await Assert.ThrowsAnyAsync<OperationCanceledException> (() => catalog.SuggestIconNameAsync (label: "Jellyfin",
                                                                                                 cancellationToken:
                                                                                                 new CancellationToken (true)));

    Assert.Equal (expected: "jellyfin",
                  actual: await catalog.SuggestIconNameAsync (label: "Jellyfin",
                                                              cancellationToken: TestContext.Current.CancellationToken));
  }

  [Theory]
  [InlineData ("")]
  [InlineData ("   ")]
  [InlineData (null)]
  public async Task SuggestIconNameAsync_ReturnsNullForBlankLabelWithoutDownloading (string? label)
  {
    StubHttpMessageHandler handler = IconCatalogTests.ServeIndex ();

    Assert.Null (await IconCatalogTests.CreateCatalog (handler)
                                       .SuggestIconNameAsync (label: label,
                                                              cancellationToken: TestContext.Current.CancellationToken));
    Assert.Empty (handler.Requests);
  }

  [Theory]
  [InlineData ("https://jellyfin.home.arpa/web/",   new[] { "jellyfin", "home" })]
  [InlineData ("https://github.com/arkane-systems", new[] { "github" })]
  [InlineData ("https://hub.docker.com/",           new[] { "hub", "docker" })]
  [InlineData ("http://router/",                    new[] { "router" })]
  [InlineData ("http://192.168.1.1/",               new string[0])]
  [InlineData ("not a url",                         new string[0])]
  public void GetHostCandidates_UsesTheFirstLabelAndTheOneBeforeTheTld (string url, string[] expected)
    => Assert.Equal (expected: expected, actual: IconCatalog.GetHostCandidates (url));

  [Fact]
  public void GetFirstWords_TakesTheFirstWordOfMultiWordNames ()
    => Assert.Equal (expected: ["QNAP", "Tailscale"],
                     actual: IconCatalog.GetFirstWords (["QNAP Turbo NAS", "Plex", null, "Tailscale Session Recorder", "Up next"]));

  /// <summary>
  ///   Creates a handler that serves a small icon index.
  /// </summary>
  /// <returns>The handler.</returns>
  private static StubHttpMessageHandler ServeIndex ()
    => new (_ => new HttpResponseMessage (HttpStatusCode.OK)
                 {
                   Content = new StringContent (content: DashboardApplicationFactory.IconIndexJson,
                                                encoding: Encoding.UTF8,
                                                mediaType: "application/json"),
                 });

  /// <summary>
  ///   Creates a catalog whose index client uses the given handler.
  /// </summary>
  /// <param name="handler">The handler that answers index requests.</param>
  /// <returns>The catalog.</returns>
  private static IconCatalog CreateCatalog (HttpMessageHandler handler)
    => new (httpClientFactory: new StubHttpClientFactory (new Dictionary<string, HttpMessageHandler>
                                                          {
                                                            [IconCatalog.ClientName] = handler,
                                                          }),
            cache: new MemoryCache (new MemoryCacheOptions ()));
}

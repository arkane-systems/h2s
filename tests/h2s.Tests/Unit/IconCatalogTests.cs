using System.Net;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="IconCatalog" />.
/// </summary>
public class IconCatalogTests
{
  [Fact]
  public async Task SuggestIconNameAsync_CachesResults ()
  {
    StubHttpMessageHandler handler = StubHttpMessageHandler.Returning (HttpStatusCode.OK);
    IconCatalog            catalog = IconCatalogTests.CreateCatalog (handler);

    _ = await catalog.SuggestIconNameAsync (label: "Jellyfin", cancellationToken: TestContext.Current.CancellationToken);
    _ = await catalog.SuggestIconNameAsync (label: "jellyfin", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Single (handler.Requests);
  }

  [Fact]
  public async Task SuggestIconNameAsync_DoesNotCacheCancelledLookups ()
  {
    StubHttpMessageHandler handler = StubHttpMessageHandler.Returning (HttpStatusCode.OK);
    IconCatalog            catalog = IconCatalogTests.CreateCatalog (handler);

    await Assert.ThrowsAnyAsync<OperationCanceledException> (() => catalog.SuggestIconNameAsync (label: "Jellyfin",
                                                                                                 cancellationToken:
                                                                                                 new CancellationToken (true)));

    Assert.Equal (expected: "jellyfin",
                  actual: await catalog.SuggestIconNameAsync (label: "Jellyfin",
                                                              cancellationToken: TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task SuggestIconNameAsync_FallsBackToGetWhenHeadIsNotAllowed ()
  {
    var handler = new StubHttpMessageHandler (r => new HttpResponseMessage (r.Method == HttpMethod.Head
                                                                              ? HttpStatusCode.MethodNotAllowed
                                                                              : HttpStatusCode.OK));

    Assert.Equal (expected: "jellyfin",
                  actual: await IconCatalogTests.CreateCatalog (handler)
                                                .SuggestIconNameAsync (label: "Jellyfin",
                                                                       cancellationToken: TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task SuggestIconNameAsync_ProbesTheCdnForTheNormalizedName ()
  {
    StubHttpMessageHandler handler = StubHttpMessageHandler.Returning (HttpStatusCode.OK);

    string? iconName = await IconCatalogTests.CreateCatalog (handler)
                                             .SuggestIconNameAsync (label: "Home Assistant",
                                                                    cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: "home-assistant", actual: iconName);

    HttpRequestMessage request = Assert.Single (handler.Requests);

    Assert.Equal (expected: HttpMethod.Head, actual: request.Method);
    Assert.Equal (expected: new Uri ("https://cdn.jsdelivr.net/gh/selfhst/icons/png/home-assistant.png"),
                  actual: request.RequestUri);
  }

  [Theory]
  [InlineData ("")]
  [InlineData ("   ")]
  [InlineData (null)]
  public async Task SuggestIconNameAsync_ReturnsNullForBlankLabel (string? label)
  {
    StubHttpMessageHandler handler = StubHttpMessageHandler.Returning (HttpStatusCode.OK);

    Assert.Null (await IconCatalogTests.CreateCatalog (handler)
                                       .SuggestIconNameAsync (label: label,
                                                              cancellationToken: TestContext.Current.CancellationToken));
    Assert.Empty (handler.Requests);
  }

  [Fact]
  public async Task SuggestIconNameAsync_ReturnsNullWhenIconIsMissing ()
    => Assert.Null (await IconCatalogTests.CreateCatalog (StubHttpMessageHandler.Returning (HttpStatusCode.NotFound))
                                          .SuggestIconNameAsync (label: "No Such App",
                                                                 cancellationToken: TestContext.Current.CancellationToken));

  [Fact]
  public async Task SuggestIconNameAsync_ReturnsNullWhenProbeFails ()
    => Assert.Null (await IconCatalogTests.CreateCatalog (StubHttpMessageHandler.Throwing (new HttpRequestException ("Offline")))
                                          .SuggestIconNameAsync (label: "Jellyfin",
                                                                 cancellationToken: TestContext.Current.CancellationToken));

  /// <summary>
  ///   Creates a catalog with an empty cache whose CDN client sends through a stub handler.
  /// </summary>
  /// <param name="handler">The handler behind the CDN client.</param>
  /// <returns>The catalog.</returns>
  private static IconCatalog CreateCatalog (HttpMessageHandler handler)
    => new (httpClientFactory: new StubHttpClientFactory (new Dictionary<string, HttpMessageHandler>
                                                          {
                                                            [IconCatalog.ClientName] = handler,
                                                          }),
            cache: new MemoryCache (new MemoryCacheOptions ()));
}

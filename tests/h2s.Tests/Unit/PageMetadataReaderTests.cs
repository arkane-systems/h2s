using System.Net;
using System.Net.Http.Headers;
using System.Text;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="PageMetadataReader" />.
/// </summary>
public class PageMetadataReaderTests
{
  [Theory]
  [InlineData ("<html><head><title>Jellyfin</title></head></html>",   "Jellyfin")]
  [InlineData ("<TITLE lang=\"en\">\n  Home\n  Assistant \n</TITLE>", "Home Assistant")]
  [InlineData ("<title>Tom &amp; Jerry &#8211; Wiki</title>",         "Tom & Jerry – Wiki")]
  [InlineData ("<title></title>",                                     null)]
  [InlineData ("<title>   </title>",                                  null)]
  [InlineData ("<html><body>No title</body></html>",                  null)]
  [InlineData ("<svg><title>Icon</title></svg><title>Page</title>",   "Icon")]
  public void ParseTitle_ReadsTheFirstTitle (string html, string? expected)
    => Assert.Equal (expected: expected, actual: PageMetadataReader.ParseTitle (html));

  [Fact]
  public void ParseTitle_TruncatesLongTitles ()
    => Assert.Equal (expected: 200,
                     actual: PageMetadataReader.ParseTitle ($"<title>{new string (c: 'x', count: 500)}</title>")!.Length);

  [Fact]
  public async Task ReadAsync_ReadsTheTitle ()
  {
    StubHttpMessageHandler handler = PageMetadataReaderTests.ServeHtml ("<title>Sonarr</title>");

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://sonarr.home.arpa/",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: "Sonarr",                              actual: metadata.Title);
    Assert.Equal (expected: new Uri ("https://sonarr.home.arpa/"), actual: Assert.Single (handler.Requests).RequestUri);
  }

  [Fact]
  public async Task ReadAsync_DecodesTheDeclaredCharacterSet ()
  {
    var handler = new StubHttpMessageHandler (_ =>
                                              {
                                                var content =
                                                  new ByteArrayContent (Encoding.Latin1.GetBytes ("<title>Café</title>"));
                                                content.Headers.ContentType =
                                                  new MediaTypeHeaderValue ("text/html") { CharSet = "iso-8859-1" };

                                                return new HttpResponseMessage (HttpStatusCode.OK) { Content = content };
                                              });

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://cafe.example/",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: "Café", actual: metadata.Title);
  }

  [Fact]
  public async Task ReadAsync_CachesSuccessfulReads ()
  {
    StubHttpMessageHandler handler = PageMetadataReaderTests.ServeHtml ("<title>Radarr</title>");
    PageMetadataReader     reader  = PageMetadataReaderTests.CreateReader (handler);

    _ = await reader.ReadAsync (url: "https://radarr.home.arpa/", cancellationToken: TestContext.Current.CancellationToken);
    _ = await reader.ReadAsync (url: "https://radarr.home.arpa/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Single (handler.Requests);
  }

  [Fact]
  public async Task ReadAsync_RetriesFailedReads ()
  {
    StubHttpMessageHandler handler = StubHttpMessageHandler.Throwing (new HttpRequestException ("Unreachable"));
    PageMetadataReader     reader  = PageMetadataReaderTests.CreateReader (handler);

    PageMetadata first =
      await reader.ReadAsync (url: "https://down.home.arpa/", cancellationToken: TestContext.Current.CancellationToken);
    _ = await reader.ReadAsync (url: "https://down.home.arpa/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Null (first.Title);
    Assert.Equal (expected: 2, actual: handler.Requests.Count);
  }

  [Fact]
  public async Task ReadAsync_IgnoresPagesThatAreNotHtml ()
  {
    var handler = new StubHttpMessageHandler (_ => new HttpResponseMessage (HttpStatusCode.OK)
                                                   {
                                                     Content = new StringContent (content: "<title>Not really</title>",
                                                                                  encoding: Encoding.UTF8,
                                                                                  mediaType: "application/json"),
                                                   });

    Assert.Null ((await PageMetadataReaderTests.CreateReader (handler)
                                               .ReadAsync (url: "https://api.home.arpa/",
                                                           cancellationToken: TestContext.Current.CancellationToken)).Title);
  }

  [Theory]
  [InlineData ("ftp://files.home.arpa/")]
  [InlineData ("file:///etc/passwd")]
  [InlineData ("/relative")]
  [InlineData ("")]
  [InlineData (null)]
  public async Task ReadAsync_FetchesOnlyHttpUrls (string? url)
  {
    StubHttpMessageHandler handler = PageMetadataReaderTests.ServeHtml ("<title>Nope</title>");

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: url,
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.Null (metadata.Title);
    Assert.Empty (handler.Requests);
  }

  /// <summary>
  ///   Creates a handler that answers every request with an HTML page.
  /// </summary>
  /// <param name="html">The page.</param>
  /// <returns>The handler.</returns>
  private static StubHttpMessageHandler ServeHtml (string html)
    => new (_ => new HttpResponseMessage (HttpStatusCode.OK)
                 {
                   Content = new StringContent (content: html, encoding: Encoding.UTF8, mediaType: "text/html"),
                 });

  /// <summary>
  ///   Creates a reader whose page client uses the given handler.
  /// </summary>
  /// <param name="handler">The handler that answers page requests.</param>
  /// <returns>The reader.</returns>
  private static PageMetadataReader CreateReader (HttpMessageHandler handler)
    => new (httpClientFactory: new StubHttpClientFactory (new Dictionary<string, HttpMessageHandler>
                                                          {
                                                            [PageMetadataReader.ClientName] = handler,
                                                          }),
            cache: new MemoryCache (new MemoryCacheOptions ()));
}

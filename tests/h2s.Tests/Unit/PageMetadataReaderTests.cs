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

    Assert.Equal (expected: "Sonarr", actual: metadata.Title);
    Assert.Equal (expected: new Uri ("https://sonarr.home.arpa/"),
                  actual: Assert.Single (PageMetadataReaderTests.PageRequests (handler)).RequestUri);
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

    Assert.Single (PageMetadataReaderTests.PageRequests (handler));
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

  [Theory]
  [InlineData ("Loading...",                  true)]
  [InlineData ("Loading…",                    true)]
  [InlineData ("401 Unauthorized",            true)]
  [InlineData ("Error 404 - Not Found",       true)]
  [InlineData ("403",                         true)]
  [InlineData ("Sign in",                     true)]
  [InlineData ("Welcome to nginx!",           true)]
  [InlineData ("React App",                   true)]
  [InlineData ("Plex",                        false)]
  [InlineData ("Home Assistant",              false)]
  [InlineData ("Dashboard - Grafana",         false)]
  [InlineData ("Proxmox Virtual Environment", false)]
  public void IsGenericTitle_SpotsTitlesThatDescribeAState (string title, bool expected)
    => Assert.Equal (expected: expected, actual: PageMetadataReader.IsGenericTitle (title));

  [Theory]
  [InlineData ("Login | Grocy", "Grocy")]
  [InlineData ("Dashboard - ESPHome", "ESPHome")]
  [InlineData ("Paperless-ngx sign in", "Paperless-ngx")]
  [InlineData ("Welcome Back! Please Log in | Ring", "Ring")]
  [InlineData ("Sign in to Gitea", "Gitea")]
  [InlineData ("Microsoft 365 - Sign into Copilot", "Microsoft 365 - Copilot")]
  [InlineData ("Sign in to your account", null)]
  [InlineData ("Gitea: Git with a cup of tea", "Gitea")]
  [InlineData ("GitHub - louislam/uptime-kuma: A fancy self-hosted monitoring tool · GitHub", "GitHub - louislam/uptime-kuma")]
  [InlineData ("Calibre-Web | Books", "Calibre-Web | Books")]
  [InlineData ("Xerox WorkCentre 6015NI - 172.16.1.39", "Xerox WorkCentre 6015NI - 172.16.1.39")]
  [InlineData ("Plex", "Plex")]
  [InlineData ("Login", null)]
  [InlineData ("Loading... | Please wait", null)]
  public void CleanTitle_DropsGenericPartsAndTaglines (string title, string? expected)
    => Assert.Equal (expected: expected, actual: PageMetadataReader.CleanTitle (title));

  [Fact]
  public void GetTitleParts_ListsTheMeaningfulParts ()
    => Assert.Equal (expected: ["Grocy", "Kitchen"], actual: PageMetadataReader.GetTitleParts ("Login | Grocy - Kitchen: stock"));

  [Theory]
  [InlineData ("""<meta http-equiv="refresh" content="0; url=/ui/">""",                       "https://nas.home.arpa/ui/")]
  [InlineData ("""<META CONTENT="5;URL='https://other.home.arpa/x'" HTTP-EQUIV="Refresh">""", "https://other.home.arpa/x")]
  [InlineData ("""<script>window.location = window.location.href.match(/(^.+\/)[^\/]*$/)[1] + 'web/index.html';</script>""",
               "https://nas.home.arpa/web/index.html")]
  [InlineData ("""<script>location.href="/admin/";</script>""",              "https://nas.home.arpa/admin/")]
  [InlineData ("""<script>window.location.replace('login.html')</script>""", "https://nas.home.arpa/login.html")]
  [InlineData ("""<meta http-equiv="refresh" content="30">""",               null)]
  [InlineData ("""<script>location.href = 'javascript:void(0)';</script>""", null)]
  [InlineData ("""<script>if (location.href == '/x') {}</script>""",         null)]
  [InlineData ("""<html><head><title>Plain</title></head></html>""",         null)]
  public void FindClientRedirect_FindsMetaRefreshesAndScriptRedirects (string html, string? expected)
    => Assert.Equal (expected: expected,
                     actual: PageMetadataReader.FindClientRedirect (html: html, pageUri: new Uri ("https://nas.home.arpa/"))
                                              ?.AbsoluteUri);

  [Fact]
  public void FindClientRedirect_IgnoresScriptsOnFullPages ()
  {
    var html = $"<script>location.href = '/elsewhere';</script>{new string (c: ' ', count: 5000)}<p>Real content</p>";

    Assert.Null (PageMetadataReader.FindClientRedirect (html: html, pageUri: new Uri ("https://nas.home.arpa/")));
  }

  [Fact]
  public async Task ReadAsync_FollowsAScriptRedirectFromAnErrorPage ()
  {
    // What Plex answers at its base URL: a 401 page whose script sends browsers to the web app.
    var handler = new StubHttpMessageHandler (request => request.RequestUri!.AbsolutePath switch
                                                         {
                                                           "/" => PageMetadataReaderTests.Html (html:
                                                                                                "<html><head><script>window.location = window.location.href.match(/(^.+\\/)[^\\/]*$/)[1] + 'web/index.html';</script><title>Unauthorized</title></head></html>",
                                                                                                status: HttpStatusCode
                                                                                                 .Unauthorized),
                                                           "/web/index.html" =>
                                                             PageMetadataReaderTests.Html ("<title>Plex</title>"),
                                                           _ => new HttpResponseMessage (HttpStatusCode.NotFound),
                                                         });

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://plex.home.arpa:32400/",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: "Plex", actual: metadata.SuggestedLabel);
  }

  [Fact]
  public async Task ReadAsync_FollowsAMetaRefresh ()
  {
    var handler = new StubHttpMessageHandler (request => request.RequestUri!.AbsolutePath == "/"
                                                           ? PageMetadataReaderTests
                                                            .Html ("""<head><meta http-equiv="refresh" content="0;url=/ui/"><title>Redirecting...</title></head>""")
                                                           : PageMetadataReaderTests.Html ("<title>OPNsense</title>"));

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://firewall.home.arpa/",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: "OPNsense", actual: metadata.Title);
  }

  [Fact]
  public async Task ReadAsync_NeverTakesAnErrorPagesTitle ()
  {
    StubHttpMessageHandler handler = new (_ => PageMetadataReaderTests.Html (html: "<title>Login - Sonarr</title>",
                                                                             status: HttpStatusCode.Unauthorized));

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://sonarr.home.arpa/",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.True (metadata.IsEmpty);
  }

  [Fact]
  public async Task ReadAsync_ReadsAppNamesWhenTheTitleIsGeneric ()
  {
    var handler = new StubHttpMessageHandler (request => request.RequestUri!.AbsolutePath switch
                                                         {
                                                           "/web/manifest.json" => new HttpResponseMessage (HttpStatusCode.OK)
                                                                                   {
                                                                                     Content =
                                                                                       new StringContent (content:
                                                                                                          """{ "short_name": "Jellyfin", "name": "Jellyfin Media Server" }""",
                                                                                                          encoding: Encoding.UTF8,
                                                                                                          mediaType:
                                                                                                          "application/manifest+json"),
                                                                                   },
                                                           _ => PageMetadataReaderTests.Html ("""
                                                                                              <head>
                                                                                                <title>Loading…</title>
                                                                                                <link crossorigin="use-credentials" rel="manifest" href="manifest.json">
                                                                                                <meta content="Jelly" name="application-name">
                                                                                                <meta property="og:site_name" content="Jellyfin">
                                                                                              </head>
                                                                                              """),
                                                         });

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://jellyfin.home.arpa/web/",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.Null (metadata.Title);
    Assert.Equal (expected: ["Jellyfin", "Jellyfin Media Server", "Jelly"], actual: metadata.AppNames);
    Assert.Equal (expected: "Jellyfin",                                     actual: metadata.SuggestedLabel);
    Assert.Contains (collection: handler.Requests,
                     filter: r => r.RequestUri == new Uri ("https://jellyfin.home.arpa/web/manifest.json"));
  }

  [Fact]
  public async Task ReadAsync_ChoosesTheLargestDeclaredIcon ()
  {
    var handler = new StubHttpMessageHandler (request => request.RequestUri!.AbsolutePath switch
                                                         {
                                                           "/app.webmanifest" => new HttpResponseMessage (HttpStatusCode.OK)
                                                                                 {
                                                                                   Content = new StringContent (content: """
                                                                                                                         { "icons": [
                                                                                                                           { "src": "/icons/maskable-512.png", "sizes": "512x512", "purpose": "maskable" },
                                                                                                                           { "src": "/icons/icon-192.png", "sizes": "192x192" }
                                                                                                                         ] }
                                                                                                                         """,
                                                                                                                encoding: Encoding
                                                                                                                 .UTF8,
                                                                                                                mediaType:
                                                                                                                "application/manifest+json"),
                                                                                 },
                                                           _ => PageMetadataReaderTests.Html ("""
                                                                                              <head>
                                                                                                <title>Sonarr</title>
                                                                                                <link rel="icon" type="image/png" sizes="32x32" href="/favicon-32.png">
                                                                                                <link rel="apple-touch-icon" href="/apple-touch-icon.png">
                                                                                                <link rel="mask-icon" href="/safari.svg" color="#000">
                                                                                                <link rel="manifest" href="/app.webmanifest">
                                                                                              </head>
                                                                                              """),
                                                         });

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://sonarr.home.arpa/",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    // The maskable icon is padded for masking and the mask icon is a silhouette, so the 192px manifest icon wins.
    Assert.Equal (expected: "https://sonarr.home.arpa/icons/icon-192.png", actual: metadata.IconUrl);
  }

  [Fact]
  public async Task ReadAsync_PrefersScalableIcons ()
  {
    StubHttpMessageHandler handler = PageMetadataReaderTests.ServeHtml ("""
                                                                        <link rel="shortcut icon" href="favicon.ico">
                                                                        <link rel="icon" type="image/svg+xml" href="logo.svg">
                                                                        """);

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://grafana.home.arpa/login",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: "https://grafana.home.arpa/logo.svg", actual: metadata.IconUrl);
  }

  [Theory]
  [InlineData ("image/x-icon", "https://router.home.arpa/favicon.ico")]
  [InlineData ("text/html",    null)]
  public async Task ReadAsync_FallsBackToTheConventionalFavicon (string faviconType, string? expected)
  {
    var handler = new StubHttpMessageHandler (request => request.RequestUri!.AbsolutePath == "/favicon.ico"
                                                           ? new HttpResponseMessage (HttpStatusCode.OK)
                                                             {
                                                               Content = new ByteArrayContent ([0, 0, 1, 0])
                                                                         {
                                                                           Headers =
                                                                           {
                                                                             ContentType = new MediaTypeHeaderValue (faviconType),
                                                                           },
                                                                         },
                                                             }
                                                           : PageMetadataReaderTests.Html ("<title>Router</title>"));

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://router.home.arpa/admin/",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: expected, actual: metadata.IconUrl);
  }

  [Fact]
  public async Task ReadAsync_FollowsAtMostTwoClientRedirects ()
  {
    // Each page refreshes to the next, forever.
    var handler = new StubHttpMessageHandler (request =>
                                              {
                                                int page = int.Parse (request.RequestUri!.AbsolutePath.Trim ('/'));

                                                return
                                                  PageMetadataReaderTests
                                                   .Html ($"""<meta http-equiv="refresh" content="0;url=/{page + 1}"><title>Page {page}</title>""");
                                              });

    PageMetadata metadata = await PageMetadataReaderTests.CreateReader (handler)
                                                         .ReadAsync (url: "https://loop.home.arpa/1",
                                                                     cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: 1 + PageMetadataReader.MaxClientRedirects,
                  actual: PageMetadataReaderTests.PageRequests (handler).Count ());
    Assert.Equal (expected: "Page 3", actual: metadata.Title);
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
  ///   Gets the requests for pages, leaving out the reader's check for a conventional <c>/favicon.ico</c>.
  /// </summary>
  /// <param name="handler">The handler that answered the requests.</param>
  /// <returns>The page requests.</returns>
  private static IEnumerable<HttpRequestMessage> PageRequests (StubHttpMessageHandler handler)
    => handler.Requests.Where (r => r.RequestUri!.AbsolutePath != "/favicon.ico");

  /// <summary>
  ///   Creates an HTML response.
  /// </summary>
  /// <param name="html">The page.</param>
  /// <param name="status">The response status.</param>
  /// <returns>The response.</returns>
  private static HttpResponseMessage Html (string html, HttpStatusCode status = HttpStatusCode.OK)
    => new (status) { Content = new StringContent (content: html, encoding: Encoding.UTF8, mediaType: "text/html") };

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

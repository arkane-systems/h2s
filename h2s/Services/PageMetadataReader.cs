using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Services;

/// <summary>
///   Reads what the link dialog can suggest from a web page: currently its title.
/// </summary>
/// <remarks>
///   Requests validate certificates as usual. A host whose certificate comes from a CA the server does not trust (such as
///   a homelab CA that hasn't been mounted into the container) simply yields no metadata.
/// </remarks>
public partial class PageMetadataReader
{
  /// <summary>
  ///   The name of the HTTP client used to fetch pages.
  /// </summary>
  public const string ClientName = "PageMetadata";

  // The title is in the <head>, so there's no need to read a large page to the end.
  private const int MaxBytes = 256 * 1024;

  private const int MaxTitleLength = 200;

  /// <summary>
  ///   The time allowed for fetching a page.
  /// </summary>
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

  private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes (10);

  private readonly IMemoryCache cache;

  private readonly IHttpClientFactory httpClientFactory;

  /// <summary>
  ///   Initializes a new instance of the <see cref="PageMetadataReader" /> class.
  /// </summary>
  /// <param name="httpClientFactory">The factory that provides the page client.</param>
  /// <param name="cache">The cache that holds recently read metadata.</param>
  public PageMetadataReader (IHttpClientFactory httpClientFactory, IMemoryCache cache)
  {
    this.httpClientFactory = httpClientFactory;
    this.cache             = cache;
  }

  /// <summary>
  ///   Reads a page's metadata.
  /// </summary>
  /// <param name="url">The page URL. Only absolute HTTP and HTTPS URLs are fetched.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>The metadata, which is empty when the page could not be read.</returns>
  public async Task<PageMetadata> ReadAsync (string? url, CancellationToken cancellationToken = default)
  {
    if (!Uri.TryCreate (uriString: url?.Trim (), uriKind: UriKind.Absolute, result: out Uri? uri) ||
        ((uri.Scheme != Uri.UriSchemeHttp) && (uri.Scheme != Uri.UriSchemeHttps)))
      return PageMetadata.Empty;

    var key = $"page-metadata:{uri.AbsoluteUri}";

    if (this.cache.TryGetValue (key: key, value: out PageMetadata? cached) && (cached != null))
      return cached;

    PageMetadata? metadata = await this.FetchAsync (uri: uri, cancellationToken: cancellationToken);

    // Only successful reads are cached, so a page that was briefly down is tried again next time.
    if (metadata == null)
      return PageMetadata.Empty;

    _ = this.cache.Set (key: key, value: metadata, absoluteExpirationRelativeToNow: PageMetadataReader.CacheDuration);

    return metadata;
  }

  /// <summary>
  ///   Extracts the title from the start of an HTML document.
  /// </summary>
  /// <param name="html">The HTML.</param>
  /// <returns>The decoded title with whitespace collapsed, or <c>null</c> when there is none.</returns>
  public static string? ParseTitle (string html)
  {
    Match match = PageMetadataReader.TitlePattern ().Match (html);

    if (!match.Success)
      return null;

    string title = PageMetadataReader.WhitespacePattern ()
                                     .Replace (input: WebUtility.HtmlDecode (match.Groups["title"].Value), replacement: " ")
                                     .Trim ();

    if (title.Length > PageMetadataReader.MaxTitleLength)
      title = title[..PageMetadataReader.MaxTitleLength].TrimEnd ();

    return title.Length > 0 ? title : null;
  }

  /// <summary>
  ///   Fetches a page and reads its metadata.
  /// </summary>
  /// <param name="uri">The page URL.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>The metadata, or <c>null</c> when the page could not be fetched or isn't HTML.</returns>
  private async Task<PageMetadata?> FetchAsync (Uri uri, CancellationToken cancellationToken)
  {
    try
    {
      HttpClient client = this.httpClientFactory.CreateClient (PageMetadataReader.ClientName);

      using var request = new HttpRequestMessage (method: HttpMethod.Get, requestUri: uri);
      request.Headers.Accept.ParseAdd ("text/html,application/xhtml+xml;q=0.9,*/*;q=0.1");

      using HttpResponseMessage response = await client.SendAsync (request: request,
                                                                   completionOption: HttpCompletionOption.ResponseHeadersRead,
                                                                   cancellationToken: cancellationToken);

      string? mediaType = response.Content.Headers.ContentType?.MediaType;

      if (!response.IsSuccessStatusCode || ((mediaType != null) && !mediaType.Contains ("html")))
        return null;

      string html = await PageMetadataReader.ReadPrefixAsync (content: response.Content, cancellationToken: cancellationToken);

      return new PageMetadata (PageMetadataReader.ParseTitle (html));
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      // An unreachable page, a TLS failure or a timeout just means there's nothing to suggest.
      return null;
    }
  }

  /// <summary>
  ///   Reads up to <see cref="MaxBytes" /> of a response and decodes it with its declared character set, or UTF-8.
  /// </summary>
  /// <param name="content">The response content.</param>
  /// <param name="cancellationToken">A token used to cancel the read.</param>
  /// <returns>The decoded text.</returns>
  private static async Task<string> ReadPrefixAsync (HttpContent content, CancellationToken cancellationToken)
  {
    await using Stream stream = await content.ReadAsStreamAsync (cancellationToken);

    var buffer = new byte[PageMetadataReader.MaxBytes];
    var length = 0;
    int read;

    while ((length < buffer.Length) &&
           ((read = await stream.ReadAsync (buffer: buffer.AsMemory (start: length), cancellationToken: cancellationToken)) > 0))
      length += read;

    return PageMetadataReader.GetEncoding (content.Headers.ContentType?.CharSet).GetString (bytes: buffer, index: 0, count: length);
  }

  /// <summary>
  ///   Gets the encoding for a declared character set.
  /// </summary>
  /// <param name="charSet">The character set from the Content-Type header, if any.</param>
  /// <returns>The encoding, or UTF-8 when the character set is missing or unknown.</returns>
  private static Encoding GetEncoding (string? charSet)
  {
    try
    {
      return string.IsNullOrWhiteSpace (charSet) ? Encoding.UTF8 : Encoding.GetEncoding (charSet.Trim ('"', ' '));
    }
    catch (ArgumentException)
    {
      return Encoding.UTF8;
    }
  }

  [GeneratedRegex (pattern: "<title[^>]*>(?<title>.*?)</title\\s*>", options: RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex TitlePattern ();

  [GeneratedRegex ("\\s+")]
  private static partial Regex WhitespacePattern ();
}

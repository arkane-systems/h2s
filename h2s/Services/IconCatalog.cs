using System.Net;
using h2s.Models;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Services;

/// <summary>
///   Looks up icons on the selfh.st icon CDN.
/// </summary>
public class IconCatalog
{
  /// <summary>
  ///   The name of the HTTP client used to query the icon CDN.
  /// </summary>
  public const string ClientName = "IconCatalog";

  /// <summary>
  ///   The time allowed for each request to the icon CDN.
  /// </summary>
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds (4);

  private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes (10);

  private readonly IMemoryCache cache;

  private readonly IHttpClientFactory httpClientFactory;

  /// <summary>
  ///   Initializes a new instance of the <see cref="IconCatalog" /> class.
  /// </summary>
  /// <param name="httpClientFactory">The factory that provides the icon CDN client.</param>
  /// <param name="cache">The cache used to avoid repeated icon availability probes.</param>
  public IconCatalog (IHttpClientFactory httpClientFactory, IMemoryCache cache)
  {
    this.httpClientFactory = httpClientFactory;
    this.cache             = cache;
  }

  /// <summary>
  ///   Suggests an icon for a link label, when an icon named after the label exists.
  /// </summary>
  /// <param name="label">The link label to derive the icon name from.</param>
  /// <param name="cancellationToken">A token that cancels the lookup.</param>
  /// <returns>The normalized icon name when a matching icon exists; otherwise, <c>null</c>.</returns>
  public async Task<string?> SuggestIconNameAsync (string? label, CancellationToken cancellationToken = default)
  {
    string iconName = Link.NormalizeIconName (label ?? string.Empty);

    if (string.IsNullOrWhiteSpace (iconName))
      return null;

    bool found = await this.cache.GetOrCreateAsync (key: $"icon-exists:{iconName}",
                                                    factory: entry =>
                                                             {
                                                               entry.AbsoluteExpirationRelativeToNow = IconCatalog.CacheDuration;

                                                               return this.IconExistsAsync (url: Link.BuildIconUrl (iconName),
                                                                                            cancellationToken: cancellationToken);
                                                             });

    return found ? iconName : null;
  }

  /// <summary>
  ///   Checks whether an icon URL exists on the CDN.
  /// </summary>
  /// <param name="url">The icon URL to probe.</param>
  /// <param name="cancellationToken">A token that cancels the probe.</param>
  /// <returns><c>true</c> when the icon exists; otherwise, <c>false</c>.</returns>
  private async Task<bool> IconExistsAsync (string url, CancellationToken cancellationToken)
  {
    HttpClient client = this.httpClientFactory.CreateClient (IconCatalog.ClientName);

    try
    {
      using var headRequest = new HttpRequestMessage (method: HttpMethod.Head, requestUri: url);
      using HttpResponseMessage headResponse = await client.SendAsync (request: headRequest,
                                                                       completionOption: HttpCompletionOption.ResponseHeadersRead,
                                                                       cancellationToken: cancellationToken);

      if (headResponse.IsSuccessStatusCode)
        return true;

      if (headResponse.StatusCode != HttpStatusCode.MethodNotAllowed)
        return false;

      using var getRequest = new HttpRequestMessage (method: HttpMethod.Get, requestUri: url);
      using HttpResponseMessage getResponse = await client.SendAsync (request: getRequest,
                                                                      completionOption: HttpCompletionOption.ResponseHeadersRead,
                                                                      cancellationToken: cancellationToken);

      return getResponse.IsSuccessStatusCode;
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      // Treat a failed or timed-out probe as "no icon", but let a cancelled request propagate rather than be cached.
      return false;
    }
  }
}

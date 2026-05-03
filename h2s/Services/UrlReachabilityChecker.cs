namespace h2s.Services;

/// <summary>
/// Provides URL reachability checking with caching to avoid repeated requests.
/// </summary>
public class UrlReachabilityChecker
{
  private readonly IHttpClientFactory _httpClientFactory;
  private static readonly Dictionary<string, (bool IsReachable, DateTime CachedAt)> _cache = new ();
  private static readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds (60);
  private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds (5);

  /// <summary>
  /// Initializes a new instance of the <see cref="UrlReachabilityChecker"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory for making requests.</param>
  public UrlReachabilityChecker (IHttpClientFactory httpClientFactory)
  {
    _httpClientFactory = httpClientFactory;
  }

  /// <summary>
  /// Checks if the specified URL is reachable by performing a HEAD request.
  /// Results are cached for 60 seconds to avoid repeated requests.
  /// </summary>
  /// <param name="url">The URL to check.</param>
  /// <returns>A tuple indicating whether the URL is reachable and any error message if unreachable.</returns>
  public async Task<(bool IsReachable, string? ErrorMessage)> CheckReachabilityAsync (string url)
  {
    if (string.IsNullOrWhiteSpace (url))
    {
      return (false, "URL is empty.");
    }

    // Validate URL format
    if (!Uri.TryCreate (url, UriKind.Absolute, out var uri))
    {
      return (false, "URL format is invalid.");
    }

    // Check cache
    lock (_cache)
    {
      if (_cache.TryGetValue (url, out var cachedResult))
      {
        if (DateTime.UtcNow - cachedResult.CachedAt < _cacheDuration)
        {
          return (cachedResult.IsReachable, cachedResult.IsReachable ? null : $"The server at {url} could not be reached. Please verify the URL is correct and the server is accessible.");
        }
        else
        {
          // Remove expired cache entry
          _ = _cache.Remove (url);
        }
      }
    }

    // Perform HEAD request
    bool isReachable;
    try
    {
      var httpClient = _httpClientFactory.CreateClient ();
      httpClient.Timeout = _requestTimeout;

      using var request = new HttpRequestMessage (HttpMethod.Head, uri);
      using var response = await httpClient.SendAsync (request, HttpCompletionOption.ResponseHeadersRead);

      isReachable = response.IsSuccessStatusCode;
    }
    catch
    {
      isReachable = false;
    }

    // Cache result
    lock (_cache)
    {
      _cache[url] = (isReachable, DateTime.UtcNow);
    }

    return (isReachable, isReachable ? null : $"The Uptime Kuma server at {url} could not be reached. Please verify the URL is correct and the server is accessible.");
  }
}

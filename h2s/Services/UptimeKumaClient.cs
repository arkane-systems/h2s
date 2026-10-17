using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Services;

/// <summary>
///   Reads Uptime Kuma: every monitor's status, response time, uptime and certificate from its <c>/metrics</c> endpoint.
/// </summary>
/// <remarks>
///   <c>/metrics</c> takes a read-only API key (Kuma 1.21 or later, under Settings → API Keys) as the password of HTTP
///   basic authentication, and reports every monitor in one request, so reads are cached briefly and shared by everything
///   that shows Kuma's data. Certificates are validated as usual.
/// </remarks>
public class UptimeKumaClient
{
  /// <summary>
  ///   The name of the HTTP client used to call Uptime Kuma.
  /// </summary>
  public const string ClientName = "UptimeKuma";

  /// <summary>
  ///   The time allowed for each request to Uptime Kuma.
  /// </summary>
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

  // Short, so the dashboard stays current, but long enough that a page of Kuma widgets costs Kuma one request.
  private static readonly TimeSpan MetricsCacheDuration = TimeSpan.FromSeconds (30);

  private readonly IMemoryCache cache;

  private readonly IHttpClientFactory httpClientFactory;

  /// <summary>
  ///   Initializes a new instance of the <see cref="UptimeKumaClient" /> class.
  /// </summary>
  /// <param name="httpClientFactory">The factory that provides the Uptime Kuma client.</param>
  /// <param name="cache">The cache that holds recent metrics.</param>
  public UptimeKumaClient (IHttpClientFactory httpClientFactory, IMemoryCache cache)
  {
    this.httpClientFactory = httpClientFactory;
    this.cache             = cache;
  }

  /// <summary>
  ///   Reads every monitor's metrics.
  /// </summary>
  /// <param name="serverUrl">The Uptime Kuma server URL.</param>
  /// <param name="apiKey">The API key.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>The monitors' metrics, or a message explaining why they couldn't be read.</returns>
  public async Task<KumaMetrics> GetMetricsAsync (string serverUrl, string apiKey, CancellationToken cancellationToken = default)
  {
    var url = $"{serverUrl.Trim ().TrimEnd ('/')}/metrics";

    // The key itself never goes into the cache's keys.
    var key = $"kuma-metrics:{url}:{Convert.ToHexString (SHA256.HashData (Encoding.UTF8.GetBytes (apiKey)))}";

    if (this.cache.TryGetValue (key: key, value: out KumaMetrics? cached) && (cached != null))
    {
      return cached;
    }

    KumaMetrics metrics = await this.FetchMetricsAsync (url: url, apiKey: apiKey, cancellationToken: cancellationToken);

    // Only successful reads are cached, so fixing the server or key takes effect straight away.
    if (metrics.Message == null)
    {
      _ = this.cache.Set (key: key, value: metrics, absoluteExpirationRelativeToNow: UptimeKumaClient.MetricsCacheDuration);
    }

    return metrics;
  }

  /// <summary>
  ///   Fetches and parses Kuma's metrics.
  /// </summary>
  /// <param name="url">The metrics URL.</param>
  /// <param name="apiKey">The API key.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>The monitors' metrics, or a message explaining why they couldn't be read.</returns>
  private async Task<KumaMetrics> FetchMetricsAsync (string url, string apiKey, CancellationToken cancellationToken)
  {
    try
    {
      HttpClient client = this.httpClientFactory.CreateClient (UptimeKumaClient.ClientName);

      using var request = new HttpRequestMessage (method: HttpMethod.Get, requestUri: url);
      request.Headers.Authorization =
        new AuthenticationHeaderValue (scheme: "Basic", parameter: Convert.ToBase64String (Encoding.UTF8.GetBytes ($":{apiKey}")));

      using HttpResponseMessage response = await client.SendAsync (request: request, cancellationToken: cancellationToken);

      if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
      {
        return KumaMetrics.Failed ("Uptime Kuma refused the API key.");
      }

      if (response.StatusCode == HttpStatusCode.NotFound)
      {
        return KumaMetrics.Failed ("Uptime Kuma has no /metrics endpoint; it needs version 1.21 or later.");
      }

      if (!response.IsSuccessStatusCode)
      {
        return KumaMetrics.Failed ($"Uptime Kuma answered {(int)response.StatusCode} {response.ReasonPhrase}.");
      }

      return KumaMetrics.Parse (await response.Content.ReadAsStringAsync (cancellationToken));
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      return KumaMetrics.Failed ("Couldn't reach Uptime Kuma.");
    }
  }
}

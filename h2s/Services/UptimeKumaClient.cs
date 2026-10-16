using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Services;

/// <summary>
///   Reads Uptime Kuma: every monitor's status, response time, uptime and certificate from its <c>/metrics</c> endpoint,
///   and whether a status page exists.
/// </summary>
/// <remarks>
///   <c>/metrics</c> takes a read-only API key (Kuma 1.21 or later, under Settings → API Keys) as the password of HTTP
///   basic authentication, and reports every monitor in one request, so reads are cached briefly and shared by everything
///   that shows Kuma's data. Status pages use their public API, which needs no credentials. Certificates are validated as
///   usual.
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

  private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes (2);

  // Short, so the dashboard stays current, but long enough that a page of Kuma widgets costs Kuma one request.
  private static readonly TimeSpan MetricsCacheDuration = TimeSpan.FromSeconds (30);

  private readonly IMemoryCache cache;

  private readonly IHttpClientFactory httpClientFactory;

  /// <summary>
  ///   Initializes a new instance of the <see cref="UptimeKumaClient" /> class.
  /// </summary>
  /// <param name="httpClientFactory">The factory that provides the Uptime Kuma client.</param>
  /// <param name="cache">The cache that holds recently read monitor lists.</param>
  public UptimeKumaClient (IHttpClientFactory httpClientFactory, IMemoryCache cache)
  {
    this.httpClientFactory = httpClientFactory;
    this.cache             = cache;
  }

  /// <summary>
  ///   Reads the monitors on a status page, grouped as the page groups them.
  /// </summary>
  /// <param name="serverUrl">The Uptime Kuma server URL.</param>
  /// <param name="slug">The status page slug.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>The monitor groups, or a message explaining why they couldn't be read.</returns>
  public async Task<KumaMonitorList> GetStatusPageMonitorsAsync (string            serverUrl,
                                                                 string            slug,
                                                                 CancellationToken cancellationToken = default)
  {
    var url = $"{serverUrl.Trim ().TrimEnd ('/')}/api/status-page/{Uri.EscapeDataString (slug.Trim ())}";
    var key = $"kuma-monitors:{url}";

    if (this.cache.TryGetValue (key: key, value: out KumaMonitorList? cached) && (cached != null))
      return cached;

    KumaMonitorList monitors = await this.FetchAsync (url: url, slug: slug.Trim (), cancellationToken: cancellationToken);

    // Only successful reads are cached, so fixing the server or slug takes effect straight away.
    if (monitors.Message == null)
      _ = this.cache.Set (key: key, value: monitors, absoluteExpirationRelativeToNow: UptimeKumaClient.CacheDuration);

    return monitors;
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
      return cached;

    KumaMetrics metrics = await this.FetchMetricsAsync (url: url, apiKey: apiKey, cancellationToken: cancellationToken);

    // Only successful reads are cached, so fixing the server or key takes effect straight away.
    if (metrics.Message == null)
      _ = this.cache.Set (key: key, value: metrics, absoluteExpirationRelativeToNow: UptimeKumaClient.MetricsCacheDuration);

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
        return KumaMetrics.Failed ("Uptime Kuma refused the API key.");

      if (response.StatusCode == HttpStatusCode.NotFound)
        return KumaMetrics.Failed ("Uptime Kuma has no /metrics endpoint; it needs version 1.21 or later.");

      if (!response.IsSuccessStatusCode)
        return KumaMetrics.Failed ($"Uptime Kuma answered {(int)response.StatusCode} {response.ReasonPhrase}.");

      return KumaMetrics.Parse (await response.Content.ReadAsStringAsync (cancellationToken));
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      return KumaMetrics.Failed ("Couldn't reach Uptime Kuma.");
    }
  }

  /// <summary>
  ///   Fetches and parses a status page's data.
  /// </summary>
  /// <param name="url">The status page API URL.</param>
  /// <param name="slug">The status page slug, for messages.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>The monitor groups, or a message explaining why they couldn't be read.</returns>
  private async Task<KumaMonitorList> FetchAsync (string url, string slug, CancellationToken cancellationToken)
  {
    try
    {
      HttpClient client = this.httpClientFactory.CreateClient (UptimeKumaClient.ClientName);

      using HttpResponseMessage response = await client.GetAsync (requestUri: url, cancellationToken: cancellationToken);

      if (response.StatusCode == HttpStatusCode.NotFound)
        return KumaMonitorList.Failed ($"Uptime Kuma has no status page \"{slug}\".");

      if (!response.IsSuccessStatusCode)
        return KumaMonitorList.Failed ($"Uptime Kuma answered {(int)response.StatusCode} {response.ReasonPhrase}.");

      await using Stream stream = await response.Content.ReadAsStreamAsync (cancellationToken);
      using JsonDocument data   = await JsonDocument.ParseAsync (utf8Json: stream, cancellationToken: cancellationToken);

      return UptimeKumaClient.ParseGroups (data.RootElement) is { } groups
               ? new KumaMonitorList (Groups: groups, Message: null)
               : KumaMonitorList.Failed ("Uptime Kuma's answer wasn't a status page.");
    }
    catch (JsonException)
    {
      return KumaMonitorList.Failed ("Uptime Kuma's answer wasn't a status page.");
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      return KumaMonitorList.Failed ("Couldn't reach Uptime Kuma.");
    }
  }

  /// <summary>
  ///   Reads the monitor groups from a status page's data: <c>publicGroupList[].{name, monitorList[].{id, name}}</c>.
  /// </summary>
  /// <param name="root">The status page data.</param>
  /// <returns>The groups, or <c>null</c> when the data has no group list.</returns>
  private static List<KumaMonitorGroup>? ParseGroups (JsonElement root)
  {
    if ((root.ValueKind != JsonValueKind.Object)                                                 ||
        !root.TryGetProperty (propertyName: "publicGroupList", value: out JsonElement groupList) ||
        (groupList.ValueKind != JsonValueKind.Array))
      return null;

    var groups = new List<KumaMonitorGroup> ();

    foreach (JsonElement group in groupList.EnumerateArray ())
    {
      if ((group.ValueKind != JsonValueKind.Object)                                               ||
          !group.TryGetProperty (propertyName: "monitorList", value: out JsonElement monitorList) ||
          (monitorList.ValueKind != JsonValueKind.Array))
        continue;

      var monitors = new List<KumaMonitor> ();

      foreach (JsonElement monitor in monitorList.EnumerateArray ())
      {
        if ((monitor.ValueKind == JsonValueKind.Object)                            &&
            monitor.TryGetProperty (propertyName: "id", value: out JsonElement id) &&
            (id.ValueKind == JsonValueKind.Number)                                 &&
            id.TryGetInt32 (out int monitorId)                                     &&
            (monitorId > 0))
        {
          monitors.Add (new KumaMonitor (Id: monitorId,
                                         Name: UptimeKumaClient.GetName (element: monitor, fallback: $"Monitor {monitorId}")));
        }
      }

      if (monitors.Count > 0)
      {
        groups.Add (new KumaMonitorGroup (Name: UptimeKumaClient.GetName (element: group, fallback: "Monitors"),
                                          Monitors: monitors));
      }
    }

    return groups;
  }

  /// <summary>
  ///   Gets an element's <c>name</c>.
  /// </summary>
  /// <param name="element">The element.</param>
  /// <param name="fallback">The name to use when it has none.</param>
  /// <returns>The name.</returns>
  private static string GetName (JsonElement element, string fallback)
    => element.TryGetProperty (propertyName: "name", value: out JsonElement name) &&
       (name.ValueKind == JsonValueKind.String)                                   &&
       !string.IsNullOrWhiteSpace (name.GetString ())
         ? name.GetString ()!.Trim ()
         : fallback;
}

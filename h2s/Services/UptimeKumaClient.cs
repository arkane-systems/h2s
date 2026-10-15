using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Services;

/// <summary>
///   Reads the monitors on an Uptime Kuma status page, for the link dialog's monitor picker.
/// </summary>
/// <remarks>
///   It uses the status page's public API (<c>GET {server}/api/status-page/{slug}</c>), which needs no credentials. Only
///   monitors on the status page appear, which is also what Kuma's badges require. Certificates are validated as usual.
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

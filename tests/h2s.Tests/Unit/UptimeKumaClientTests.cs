using System.Net;
using System.Text;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="UptimeKumaClient" />.
/// </summary>
public class UptimeKumaClientTests
{
  /// <summary>
  ///   A trimmed status page answer, as Uptime Kuma's <c>/api/status-page/{slug}</c> returns it.
  /// </summary>
  private const string StatusPageJson = """
                                        {
                                          "config": { "slug": "main", "title": "Homelab" },
                                          "incidents": [],
                                          "publicGroupList": [
                                            { "id": 1, "name": "Compute", "weight": 1, "monitorList": [
                                              { "id": 1, "name": "Proxmox", "type": "http" },
                                              { "id": 3, "name": "Traefik", "type": "http" }
                                            ] },
                                            { "id": 2, "name": "Empty", "weight": 2, "monitorList": [] },
                                            { "id": 3, "name": "Media", "weight": 3, "monitorList": [
                                              { "id": 11, "name": "Jellyfin", "type": "http" },
                                              { "id": "bad", "name": "Broken" }
                                            ] }
                                          ],
                                          "maintenanceList": []
                                        }
                                        """;

  [Fact]
  public async Task GetStatusPageMonitorsAsync_ReadsTheGroupsInOrder ()
  {
    StubHttpMessageHandler handler =
      UptimeKumaClientTests.Serve (status: HttpStatusCode.OK, body: UptimeKumaClientTests.StatusPageJson);

    KumaMonitorList list = await UptimeKumaClientTests.CreateClient (handler)
                                                      .GetStatusPageMonitorsAsync (serverUrl: "https://status.home.arpa/",
                                                                                   slug: "main",
                                                                                   cancellationToken: TestContext.Current
                                                                                                                 .CancellationToken);

    Assert.Null (list.Message);
    Assert.Equal (expected: ["Compute", "Media"],                         actual: list.Groups.Select (g => g.Name));
    Assert.Equal (expected: [1, 3],                                       actual: list.Groups[0].Monitors.Select (m => m.Id));
    Assert.Equal (expected: [new KumaMonitor (Id: 11, Name: "Jellyfin")], actual: list.Groups[1].Monitors);
    Assert.Equal (expected: new Uri ("https://status.home.arpa/api/status-page/main"),
                  actual: Assert.Single (handler.Requests).RequestUri);
  }

  [Fact]
  public async Task GetStatusPageMonitorsAsync_CachesSuccessfulReads ()
  {
    StubHttpMessageHandler handler =
      UptimeKumaClientTests.Serve (status: HttpStatusCode.OK, body: UptimeKumaClientTests.StatusPageJson);
    UptimeKumaClient client = UptimeKumaClientTests.CreateClient (handler);

    _ = await client.GetStatusPageMonitorsAsync (serverUrl: "https://status.home.arpa",
                                                 slug: "main",
                                                 cancellationToken: TestContext.Current.CancellationToken);
    _ = await client.GetStatusPageMonitorsAsync (serverUrl: "https://status.home.arpa",
                                                 slug: "main",
                                                 cancellationToken: TestContext.Current.CancellationToken);

    Assert.Single (handler.Requests);
  }

  [Theory]
  [InlineData (HttpStatusCode.NotFound,            "{}",                 "Uptime Kuma has no status page \"main\".")]
  [InlineData (HttpStatusCode.InternalServerError, "{}",                 "Uptime Kuma answered 500 Internal Server Error.")]
  [InlineData (HttpStatusCode.OK,                  "<html></html>",      "Uptime Kuma's answer wasn't a status page.")]
  [InlineData (HttpStatusCode.OK,                  """{ "ok": true }""", "Uptime Kuma's answer wasn't a status page.")]
  public async Task GetStatusPageMonitorsAsync_ExplainsFailures (HttpStatusCode status, string body, string message)
  {
    StubHttpMessageHandler handler = UptimeKumaClientTests.Serve (status: status, body: body);
    UptimeKumaClient       client  = UptimeKumaClientTests.CreateClient (handler);

    KumaMonitorList list = await client.GetStatusPageMonitorsAsync (serverUrl: "https://status.home.arpa",
                                                                    slug: "main",
                                                                    cancellationToken: TestContext.Current.CancellationToken);
    _ = await client.GetStatusPageMonitorsAsync (serverUrl: "https://status.home.arpa",
                                                 slug: "main",
                                                 cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: message, actual: list.Message);
    Assert.Empty (list.Groups);

    // Failures aren't cached, so fixing Kuma or the slug shows up straight away.
    Assert.Equal (expected: 2, actual: handler.Requests.Count);
  }

  [Fact]
  public async Task GetStatusPageMonitorsAsync_ReportsAnUnreachableServer ()
  {
    StubHttpMessageHandler handler = StubHttpMessageHandler.Throwing (new HttpRequestException ("Connection refused"));

    KumaMonitorList list = await UptimeKumaClientTests.CreateClient (handler)
                                                      .GetStatusPageMonitorsAsync (serverUrl: "https://status.home.arpa",
                                                                                   slug: "main",
                                                                                   cancellationToken: TestContext.Current
                                                                                                                 .CancellationToken);

    Assert.Equal (expected: "Couldn't reach Uptime Kuma.", actual: list.Message);
  }

  /// <summary>
  ///   Creates a handler that answers every request with the given status and body.
  /// </summary>
  /// <param name="status">The response status.</param>
  /// <param name="body">The response body.</param>
  /// <returns>The handler.</returns>
  private static StubHttpMessageHandler Serve (HttpStatusCode status, string body)
    => new (_ => new HttpResponseMessage (status)
                 {
                   Content = new StringContent (content: body, encoding: Encoding.UTF8, mediaType: "application/json"),
                 });

  /// <summary>
  ///   Creates a client whose Kuma requests use the given handler.
  /// </summary>
  /// <param name="handler">The handler that answers Kuma requests.</param>
  /// <returns>The client.</returns>
  private static UptimeKumaClient CreateClient (HttpMessageHandler handler)
    => new (httpClientFactory: new StubHttpClientFactory (new Dictionary<string, HttpMessageHandler>
                                                          {
                                                            [UptimeKumaClient.ClientName] = handler,
                                                          }),
            cache: new MemoryCache (new MemoryCacheOptions ()));
}

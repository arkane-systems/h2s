using System.Net;
using System.Text.Json;
using h2s.Models;
using h2s.Panels;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for status bars: their items as widgets, edited through the panel handlers, previewed, and several bars on one
///   dashboard.
/// </summary>
public class StatusBarTests
{
  private const string EditorPath = "/Admin/Editor";

  private const string ComputeItem =
    """[{ "kind": "uptime-kuma/monitor", "label": " Compute ", "options": { "monitorId": "1", "showResponseTime": "false", "showUptime": "true" } }]""";

  [Fact]
  public async Task WidgetKinds_OffersKindsThatAllowAStatusBar ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    JsonElement kinds = await WidgetHandlerTests.GetJsonAsync (client: factory.CreateClient (),
                                                               query: "handler=WidgetKinds&placement=statusBar");

    Assert.Equal (expected: ["uptime-kuma/monitor"],
                  actual: kinds.EnumerateArray ().Select (k => k.GetProperty ("reference").GetString ()));
  }

  [Fact]
  public async Task CreatePanel_AddsASecondStatusBar_WithItsItems ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    HttpResponseMessage response = await StatusBarTests.PostAsync (factory: factory,
                                                                   handler: "CreatePanel",
                                                                   ("kind", StatusBarPanelKind.KindKey),
                                                                   ("zone", "Bottom"),
                                                                   ("options[title]", "Home"),
                                                                   ("options[linkUrl]", ""),
                                                                   ("widgets", StatusBarTests.ComputeItem));

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    List<Panel> bars = await StatusBarTests.GetStatusBarsAsync (factory);
    Panel       home = bars.Single (p => p.Zone == PanelZone.Bottom);
    Widget      item = Assert.Single (home.Widgets);

    Assert.Equal (expected: 2,                                      actual: bars.Count);
    Assert.Equal (expected: """{"title":"Home","linkUrl":""}""",    actual: home.OptionsJson);
    Assert.Equal (expected: ("Compute", (int?)null, (int?)home.Id), actual: (item.Label, item.LinkId, item.PanelId));
    Assert.Equal (expected: """{"monitorId":1,"showResponseTime":false,"showUptime":true,"uptimeWindow":""}""",
                  actual: item.OptionsJson);
  }

  [Fact]
  public async Task UpdatePanel_ReplacesTheItems_AndDeletesTheOnesLeftOut ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    int barId = (await StatusBarTests.GetStatusBarsAsync (factory)).Single ().Id;

    _ = await StatusBarTests.PostAsync (factory: factory,
                                        handler: "UpdatePanel",
                                        ("id", $"{barId}"),
                                        ("widgets",
                                         """
                                         [
                                           { "kind": "uptime-kuma/monitor", "label": "A", "options": { "monitorId": "1" } },
                                           { "kind": "uptime-kuma/monitor", "label": "B", "options": { "monitorId": "2" } }
                                         ]
                                         """));
    Widget kept = (await StatusBarTests.GetStatusBarsAsync (factory)).Single ().Widgets.Single (w => w.Label == "B");

    HttpResponseMessage response = await StatusBarTests.PostAsync (factory: factory,
                                                                   handler: "UpdatePanel",
                                                                   ("id", $"{barId}"),
                                                                   ("widgets",
                                                                    $$"""[{ "id": {{kept.Id}}, "kind": "uptime-kuma/monitor", "label": "Bee", "options": { "monitorId": "2" } }]"""));

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    Widget item = Assert.Single ((await StatusBarTests.GetStatusBarsAsync (factory)).Single ().Widgets);

    Assert.Equal (expected: (kept.Id, "Bee", 0), actual: (item.Id, item.Label, item.SortOrder));
    Assert.Equal (expected: 1,                   actual: await StatusBarTests.CountWidgetsAsync (factory));
  }

  [Theory]
  [InlineData (21, "A status bar can have at most 20 widgets.")]
  [InlineData (1,  "Widget 1 (Monitor status): the label can be at most 100 characters.")]
  public async Task UpdatePanel_RefusesItemsThatDontFit (int count, string detail)
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    int barId = (await StatusBarTests.GetStatusBarsAsync (factory)).Single ().Id;

    string label = count == 1 ? new string (c: 'x', count: 101) : "Item";
    var widgets =
      $"[{string.Join (separator: ',', values: Enumerable.Repeat (element: $$"""{ "kind": "uptime-kuma/monitor", "label": "{{label}}", "options": { "monitorId": "1" } }""", count: count))}]";

    HttpResponseMessage response = await StatusBarTests.PostAsync (factory: factory,
                                                                   handler: "UpdatePanel",
                                                                   ("id", $"{barId}"),
                                                                   ("widgets", widgets));

    await FormPoster.AssertProblemAsync (response: response, status: HttpStatusCode.BadRequest, detail: detail);
  }

  [Fact]
  public async Task DeletePanel_TakesItsItemsWithIt_AndUndoBringsThemBack ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    int barId = (await StatusBarTests.GetStatusBarsAsync (factory)).Single ().Id;

    _ = await StatusBarTests.PostAsync (factory: factory,
                                        handler: "UpdatePanel",
                                        ("id", $"{barId}"),
                                        ("widgets", StatusBarTests.ComputeItem));

    JsonElement deleted =
      await FormPoster.ReadJsonAsync (await StatusBarTests.PostAsync (factory: factory,
                                                                      handler: "DeletePanel",
                                                                      ("id", $"{barId}")));

    Assert.Equal (expected: 0, actual: await StatusBarTests.CountWidgetsAsync (factory));

    HttpResponseMessage restored = await StatusBarTests.PostAsync (factory: factory,
                                                                   handler: "RestorePanel",
                                                                   ("snapshot", deleted.GetProperty ("restore").GetRawText ()));

    Assert.Equal (expected: HttpStatusCode.OK, actual: restored.StatusCode);
    Assert.Equal (expected: ["Compute"],
                  actual: (await StatusBarTests.GetStatusBarsAsync (factory)).Single ().Widgets.Select (w => w.Label));
  }

  [Fact]
  public async Task PanelPreview_ShowsTheBar_WithItsItemsReadOneByOne ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    HttpClient client = factory.CreateClient ();

    string empty = await StatusBarTests.GetTextAsync (client: client,
                                                      query:
                                                      "handler=PanelPreview&kind=status-bar&widgets=%5B%5D&options%5Btitle%5D=Home");

    Assert.Contains (expectedSubstring: "No items yet.", actualString: empty);

    string bar = await StatusBarTests.GetTextAsync (client: client,
                                                    query: "handler=PanelPreview&kind=status-bar&options%5Btitle%5D=Home&widgets=" +
                                                           Uri.EscapeDataString (StatusBarTests.ComputeItem));

    Assert.Contains (expectedSubstring: """<div class="status-bar-title">Home</div>""",    actualString: bar);
    Assert.Contains (expectedSubstring: """<div class="status-bar-label">Compute</div>""", actualString: bar);
    Assert.Contains (expectedSubstring: """data-widget-preview="0" """,                    actualString: bar);
    Assert.DoesNotContain (expectedSubstring: "data-widget-id", actualString: bar);

    string item = await StatusBarTests.GetTextAsync (client: client,
                                                     query: "handler=WidgetPreview&placement=statusBar&index=0&widgets=" +
                                                            Uri.EscapeDataString (StatusBarTests.ComputeItem));

    Assert.Contains (expectedSubstring: "Infrastructure is up", actualString: item);
  }

  [Fact]
  public async Task PanelPreview_SaysWhatsWrongWithTheOptions ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string note = await StatusBarTests.GetTextAsync (client: factory.CreateClient (),
                                                     query:
                                                     "handler=PanelPreview&kind=status-bar&options%5BlinkUrl%5D=ftp%3A%2F%2Fx");

    Assert.Contains (expectedSubstring: "Link must be an HTTP or HTTPS URL.", actualString: note);

    using HttpResponseMessage unknown =
      await factory.CreateClient ()
                   .GetAsync (requestUri: $"{StatusBarTests.EditorPath}?handler=PanelPreview&kind=weather",
                              cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.NotFound, actual: unknown.StatusCode);
  }

  /// <summary>
  ///   Posts to an Editor handler.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="handler">The handler.</param>
  /// <param name="fields">The form fields.</param>
  /// <returns>The response.</returns>
  private static async Task<HttpResponseMessage> PostAsync (DashboardApplicationFactory         factory,
                                                            string                              handler,
                                                            params (string Key, string Value)[] fields)
    => await FormPoster.PostAsync (client: factory.CreateClient (),
                                   pagePath: StatusBarTests.EditorPath,
                                   handler: handler,
                                   fields: [.. fields.Select (f => new KeyValuePair<string, string> (key: f.Key, value: f.Value))]);

  /// <summary>
  ///   Gets text from an Editor handler, asserting success.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="query">The query string, including the handler.</param>
  /// <returns>The text.</returns>
  private static async Task<string> GetTextAsync (HttpClient client, string query)
  {
    using HttpResponseMessage response = await client.GetAsync (requestUri: $"{StatusBarTests.EditorPath}?{query}",
                                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);
  }

  /// <summary>
  ///   Loads the status bars, with their widgets in order.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The status bars.</returns>
  private static async Task<List<Panel>> GetStatusBarsAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      List<Panel> bars = await context.Panels
                                      .AsNoTracking ()
                                      .Include (p => p.Widgets)
                                      .Where (p => p.Kind == StatusBarPanelKind.KindKey)
                                      .OrderBy (p => p.Id)
                                      .ToListAsync (TestContext.Current.CancellationToken);

      bars.ForEach (b => b.Widgets.Sort ((x, y) => x.SortOrder.CompareTo (y.SortOrder)));

      return bars;
    }
  }

  /// <summary>
  ///   Counts the database's widgets.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>How many there are.</returns>
  private static async Task<int> CountWidgetsAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.Widgets.CountAsync (TestContext.Current.CancellationToken);
    }
  }
}

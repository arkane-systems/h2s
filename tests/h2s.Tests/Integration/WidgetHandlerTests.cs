using System.Net;
using System.Text;
using System.Text.Json;
using h2s.Models;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for widgets end to end: saving them with links, rendering and reading them, Undo, removing their connection,
///   and backups.
/// </summary>
public class WidgetHandlerTests
{
  private const string EditorPath = "/Admin/Editor";

  [Fact]
  public async Task WidgetKinds_ListsKindsOnlyOnceTheirIntegrationIsSetUp ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    Assert.Empty ((await WidgetHandlerTests.GetJsonAsync (client: client, query: "handler=WidgetKinds")).EnumerateArray ());

    await using var configured = new DashboardApplicationFactory ();
    await configured.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    JsonElement kind =
      Assert.Single ((await WidgetHandlerTests.GetJsonAsync (client: configured.CreateClient (), query: "handler=WidgetKinds"))
                    .EnumerateArray ());

    Assert.Equal (expected: ("uptime-kuma/monitor", "Uptime Kuma: Monitor status", "status", 60),
                  actual: (kind.GetProperty ("reference").GetString (), kind.GetProperty ("displayName").GetString (),
                           kind.GetProperty ("role").GetString (), kind.GetProperty ("defaultRefreshSeconds").GetInt32 ()));
  }

  [Fact]
  public async Task Links_SaveTheirWidgets_AndTheDialogGetsThemBack ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    HttpClient client = factory.CreateClient ();

    int linkId = await WidgetHandlerTests.CreateLinkAsync (client: client,
                                                           factory: factory,
                                                           widgets: """
                                                                    [
                                                                      { "kind": "uptime-kuma/monitor", "options": { "monitorId": "11" } },
                                                                      { "kind": "uptime-kuma/monitor", "options": { "monitorId": "12", "uptimeWindow": "30d" }, "refreshSeconds": "30" }
                                                                    ]
                                                                    """);

    JsonElement widgets =
      (await WidgetHandlerTests.GetJsonAsync (client: client, query: $"handler=Link&id={linkId}")).GetProperty ("widgets");

    Assert.Equal (expected: [(11, "", JsonValueKind.Null), (12, "30d", JsonValueKind.Number)],
                  actual: widgets.EnumerateArray ()
                                 .Select (w => (w.GetProperty ("values").GetProperty ("monitorId").GetInt32 (),
                                                w.GetProperty ("values").GetProperty ("uptimeWindow").GetString (),
                                                w.GetProperty ("refreshSeconds").ValueKind)));

    // Saved again with the second only, now first: the first is deleted and the second keeps its ID.
    int secondId = widgets[1].GetProperty ("id").GetInt32 ();
    HttpResponseMessage updated = await WidgetHandlerTests.UpdateLinkAsync (client: client,
                                                                            factory: factory,
                                                                            linkId: linkId,
                                                                            widgets:
                                                                            $$"""[{ "id": {{secondId}}, "kind": "uptime-kuma/monitor", "options": { "monitorId": "13" } }]""");

    Assert.Equal (expected: HttpStatusCode.OK, actual: updated.StatusCode);
    Assert.Equal (expected: [(secondId, 0, """{"monitorId":13,"showResponseTime":false,"showUptime":false,"uptimeWindow":""}""")],
                  actual: await WidgetHandlerTests.DescribeWidgetsAsync (factory));

    // A form without the field leaves the widgets alone.
    _ = await WidgetHandlerTests.UpdateLinkAsync (client: client, factory: factory, linkId: linkId, widgets: null);

    Assert.Single (await WidgetHandlerTests.DescribeWidgetsAsync (factory));
  }

  [Fact]
  public async Task Links_RefuseInvalidWidgets ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: WidgetHandlerTests.EditorPath,
                                                               handler: "CreateLink",
                                                               fields: await WidgetHandlerTests.LinkFieldsAsync (factory: factory,
                                                                                                                 widgets:
                                                                                                                 """[{ "kind": "uptime-kuma/monitor", "options": {} }]"""));

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Widget 1 (Monitor status): Monitor is required.");
  }

  [Fact]
  public async Task Dashboard_ShowsAPlaceholder_ThenTheWidgetReadsAndIsKept ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    HttpClient client = factory.CreateClient ();

    _ = await WidgetHandlerTests.CreateLinkAsync (client: client,
                                                  factory: factory,
                                                  widgets:
                                                  """[{ "kind": "uptime-kuma/monitor", "options": { "monitorId": "11", "showResponseTime": "true", "showUptime": "true" } }]""");
    int widgetId = (await WidgetHandlerTests.DescribeWidgetsAsync (factory))[0].Id;

    string page = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: $"""data-widget-id="{widgetId}" """, actualString: page);
    Assert.Contains (expectedSubstring: """widget-pending""",                actualString: page);

    using HttpResponseMessage widget = await client.GetAsync (requestUri: $"/widgets/{widgetId}",
                                                              cancellationToken: TestContext.Current.CancellationToken);
    string html = await widget.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: widget.StatusCode);
    Assert.Equal (expected: "no-store",        actual: widget.Headers.CacheControl?.ToString ());
    Assert.Contains (expectedSubstring: "link-widget link-widget-status widget-error", actualString: html);
    Assert.Contains (expectedSubstring: "Jellyfin is down: 42 ms.",                    actualString: html);

    // The page now shows the kept result, without a placeholder.
    string again = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.DoesNotContain (expectedSubstring: "widget-pending", actualString: again);
    Assert.Contains (expectedSubstring: "42 ms", actualString: again);

    using HttpResponseMessage missing =
      await client.GetAsync (requestUri: "/widgets/9999", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.NotFound, actual: missing.StatusCode);
  }

  [Fact]
  public async Task DeletingALink_ThenUndo_BringsItsWidgetsBack ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    HttpClient client = factory.CreateClient ();

    int linkId = await WidgetHandlerTests.CreateLinkAsync (client: client,
                                                           factory: factory,
                                                           widgets:
                                                           """[{ "kind": "uptime-kuma/monitor", "options": { "monitorId": "11" }, "refreshSeconds": "120" }]""");

    HttpResponseMessage deleted = await FormPoster.PostAsync (client: client,
                                                              pagePath: WidgetHandlerTests.EditorPath,
                                                              handler: "DeleteLink",
                                                              fields:
                                                              [
                                                                new KeyValuePair<string, string> (key: "id", value: $"{linkId}"),
                                                              ]);
    JsonElement restore = (await FormPoster.ReadJsonAsync (deleted)).GetProperty ("restore");

    Assert.Empty (await WidgetHandlerTests.DescribeWidgetsAsync (factory));

    HttpResponseMessage restored = await FormPoster.PostAsync (client: client,
                                                               pagePath: WidgetHandlerTests.EditorPath,
                                                               handler: "RestoreLink",
                                                               fields:
                                                               [
                                                                 new KeyValuePair<string, string> (key: "snapshot",
                                                                                                   value: restore.GetRawText ()),
                                                               ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: restored.StatusCode);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Widget widget = await context.Widgets.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal (expected: ("monitor", 120), actual: (widget.Kind, widget.RefreshSeconds ?? 0));
    }
  }

  [Fact]
  public async Task RemovingAConnection_DeletesItsWidgets_AndTheCardCountsThem ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    HttpClient client = factory.CreateClient ();

    _ = await WidgetHandlerTests.CreateLinkAsync (client: client,
                                                  factory: factory,
                                                  widgets:
                                                  """[{ "kind": "uptime-kuma/monitor", "options": { "monitorId": "11" } }, { "kind": "uptime-kuma/monitor", "options": { "monitorId": "12" } }]""");

    string settings =
      await client.GetStringAsync (requestUri: "/Admin/Settings", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "\"widgetCount\":2", actualString: settings);

    HttpResponseMessage removed = await FormPoster.PostAsync (client: client,
                                                              pagePath: "/Admin/Settings",
                                                              handler: "RemoveConnection",
                                                              fields:
                                                              [
                                                                new KeyValuePair<string, string> (key: "integration",
                                                                                                  value: "uptime-kuma"),
                                                              ]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: removed.StatusCode);
    Assert.Empty (await WidgetHandlerTests.DescribeWidgetsAsync (factory));
  }

  [Fact]
  public async Task Backups_CarryWidgets_ByTheirConnectionsName ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    HttpClient client = factory.CreateClient ();

    _ = await WidgetHandlerTests.CreateLinkAsync (client: client,
                                                  factory: factory,
                                                  widgets:
                                                  """[{ "kind": "uptime-kuma/monitor", "options": { "monitorId": "11" }, "refreshSeconds": "30" }]""");

    string json = await client.GetStringAsync (requestUri: "/Admin/Settings?handler=Export",
                                               cancellationToken: TestContext.Current.CancellationToken);
    var          backup = JsonSerializer.Deserialize<DashboardBackup> (json: json, options: DashboardBackup.JsonOptions);
    BackupWidget widget = Assert.Single (backup!.Categories!.SelectMany (c => c.Links!).SelectMany (l => l.Widgets ?? []));

    Assert.Equal (expected: ("uptime-kuma/monitor", "Uptime Kuma", 30),
                  actual: (widget.Kind, widget.Connection, widget.RefreshSeconds ?? 0));

    // Importing the file back recreates the widget on the imported connection.
    HttpResponseMessage imported = await FormPoster.PostFileAsync (client: client,
                                                                   pagePath: "/Admin/Settings",
                                                                   handler: "Import",
                                                                   fieldName: "file",
                                                                   fileName: "dashboard.json",
                                                                   contents: Encoding.UTF8.GetBytes (json));

    Assert.Equal (expected: HttpStatusCode.OK, actual: imported.StatusCode);
    Assert.Equal (expected: 1, actual: (await FormPoster.ReadJsonAsync (imported)).GetProperty ("widgets").GetInt32 ());
    Assert.Single (await WidgetHandlerTests.DescribeWidgetsAsync (factory));

    // A widget naming a connection the file doesn't have is refused.
    string broken = json.Replace (oldValue: "\"connection\": \"Uptime Kuma\"", newValue: "\"connection\": \"Elsewhere\"");
    HttpResponseMessage refused = await FormPoster.PostFileAsync (client: client,
                                                                  pagePath: "/Admin/Settings",
                                                                  handler: "PreviewImport",
                                                                  fieldName: "file",
                                                                  fileName: "dashboard.json",
                                                                  contents: Encoding.UTF8.GetBytes (broken));

    Assert.Equal (expected: HttpStatusCode.BadRequest, actual: refused.StatusCode);
    Assert.Contains (expectedSubstring: "widget 1: the file has no Uptime Kuma connection called “Elsewhere”.",
                     actualString: (await FormPoster.ReadJsonAsync (refused)).GetProperty ("detail").GetString ());
  }

  /// <summary>
  ///   Creates a link in the Media category, with widgets.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="factory">The application factory.</param>
  /// <param name="widgets">The widgets, as the link dialog posts them.</param>
  /// <returns>The new link's ID.</returns>
  internal static async Task<int> CreateLinkAsync (HttpClient client, DashboardApplicationFactory factory, string widgets)
  {
    HttpResponseMessage response = await FormPoster.PostAsync (client: client,
                                                               pagePath: WidgetHandlerTests.EditorPath,
                                                               handler: "CreateLink",
                                                               fields: await WidgetHandlerTests.LinkFieldsAsync (factory: factory,
                                                                                                                 widgets: widgets));

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return (await FormPoster.ReadJsonAsync (response)).GetProperty ("id").GetInt32 ();
  }

  /// <summary>
  ///   Updates the test link, with or without a widgets field.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="factory">The application factory.</param>
  /// <param name="linkId">The link.</param>
  /// <param name="widgets">The widgets, or <c>null</c> to leave the field out.</param>
  /// <returns>The response.</returns>
  private static async Task<HttpResponseMessage> UpdateLinkAsync (HttpClient                  client,
                                                                  DashboardApplicationFactory factory,
                                                                  int                         linkId,
                                                                  string?                     widgets)
    => await FormPoster.PostAsync (client: client,
                                   pagePath: WidgetHandlerTests.EditorPath,
                                   handler: "UpdateLink",
                                   fields:
                                   [
                                     new KeyValuePair<string, string> (key: "id", value: $"{linkId}"),
                                     .. await WidgetHandlerTests.LinkFieldsAsync (factory: factory, widgets: widgets),
                                   ]);

  /// <summary>
  ///   Builds a test link's fields.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="widgets">The widgets, or <c>null</c> to leave the field out.</param>
  /// <returns>The fields.</returns>
  private static async Task<KeyValuePair<string, string>[]> LinkFieldsAsync (DashboardApplicationFactory factory, string? widgets)
  {
    var (scope, context) = factory.CreateContext ();
    int categoryId;

    using (scope)
    {
      categoryId = await context.Categories.Where (c => c.Name == "Media")
                                .Select (c => c.Id)
                                .SingleAsync (TestContext.Current.CancellationToken);
    }

    return
    [
      new KeyValuePair<string, string> (key: "categoryId", value: $"{categoryId}"),
      new KeyValuePair<string, string> (key: "label",      value: "Widgeted"),
      new KeyValuePair<string, string> (key: "url",        value: "https://widgeted.home.arpa/"),
      .. widgets == null ? [] : new[] { new KeyValuePair<string, string> (key: "widgets", value: widgets) },
    ];
  }

  /// <summary>
  ///   Describes the database's widgets.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>Each widget's ID, position and options.</returns>
  private static async Task<List<(int Id, int SortOrder, string OptionsJson)>> DescribeWidgetsAsync (
    DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return
      [
        .. (await context.Widgets.AsNoTracking ().OrderBy (w => w.SortOrder).ToListAsync (TestContext.Current.CancellationToken))
       .Select (w => (w.Id, w.SortOrder, w.OptionsJson)),
      ];
    }
  }

  /// <summary>
  ///   Gets JSON from an editor handler, asserting success.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="query">The query string, including the handler.</param>
  /// <returns>The JSON.</returns>
  internal static async Task<JsonElement> GetJsonAsync (HttpClient client, string query)
  {
    HttpResponseMessage response = await client.GetAsync (requestUri: $"{WidgetHandlerTests.EditorPath}?{query}",
                                                          cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return await FormPoster.ReadJsonAsync (response);
  }
}

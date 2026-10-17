using h2s.Integrations;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Services;
using h2s.Tests.Infrastructure;
using h2s.Widgets;
using h2s.Widgets.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for the Uptime Kuma monitor widget, <see cref="WidgetForm" /> and <see cref="WidgetFetcher" />.
/// </summary>
public class WidgetTests
{
  private static readonly KumaMonitorWidgetKind Monitor = new ();

  /// <summary>
  ///   The catalog of the built-in kinds plus a test-only app kind.
  /// </summary>
  private static readonly WidgetCatalog Catalog =
    new (new IntegrationCatalog ([new UptimeKumaIntegration (), new AppIntegration ()]));

  [Theory]
  [InlineData (11, """{"monitorId":11}""", WidgetStatus.Error, new[] { "42 ms" }, "Jellyfin is down: 42 ms.")]
  [InlineData (1,  """{"monitorId":1}""",  WidgetStatus.Ok, new[] { "99.86%" }, "Infrastructure is up: 99.86% uptime over 1 day.")]
  [InlineData (1,
               """{"monitorId":1,"uptimeWindow":"30d"}""",
               WidgetStatus.Ok,
               new[] { "99.98%" },
               "Infrastructure is up: 99.98% uptime over 30 days.")]
  [InlineData (11, """{"monitorId":11,"showResponseTime":false}""", WidgetStatus.Error, new string[0], "Jellyfin is down.")]
  public async Task KumaMonitor_ReadsTheMonitorFromTheMetrics (int          monitorId,
                                                               string       optionsJson,
                                                               WidgetStatus status,
                                                               string[]     stats,
                                                               string       description)
  {
    _ = monitorId;

    WidgetResult result = await WidgetTests.FetchAsync (optionsJson: optionsJson, apiKey: TestConnections.KumaApiKey);

    Assert.Equal (expected: status,      actual: result.Status);
    Assert.Equal (expected: stats,       actual: result.Stats.Select (s => s.Text));
    Assert.Equal (expected: description, actual: result.Description);
  }

  [Theory]
  [InlineData ("""{"monitorId":99}""", TestConnections.KumaApiKey, "Uptime Kuma doesn't report a monitor 99.")]
  [InlineData ("""{"monitorId":11}""", "wrong", "Uptime Kuma refused the API key.")]
  [InlineData ("""{"monitorId":11}""", null, "Uptime Kuma has no API key; add one in Settings → Integrations.")]
  public async Task KumaMonitor_SaysWhyItCantBeRead (string optionsJson, string? apiKey, string description)
  {
    WidgetResult result = await WidgetTests.FetchAsync (optionsJson: optionsJson, apiKey: apiKey);

    Assert.Equal (expected: WidgetStatus.Unknown, actual: result.Status);
    Assert.Equal (expected: description,          actual: result.Description);
  }

  [Theory]
  [InlineData (1,       "100")]
  [InlineData (0.99996, "99.99")]
  [InlineData (0.9986,  "99.86")]
  [InlineData (0.98015, "98.0")]
  [InlineData (0.5,     "50.0")]
  public void FormatPercent_NeverRoundsUpToAMisleading100 (double ratio, string expected)
    => Assert.Equal (expected: expected, actual: KumaMonitorWidgetKind.FormatPercent (ratio));

  [Fact]
  public void WidgetForm_ReadsValidWidgets_AndUsesTheIntegrationsConnection ()
  {
    Widget saved = new () { Id = 7, ConnectionId = 3, Kind = "monitor", OptionsJson = """{"monitorId":2}""" };

    Assert.True (condition: WidgetForm.TryRead (json: """
                                                      [
                                                        { "id": 7, "kind": "uptime-kuma/monitor", "options": { "monitorId": " 12 ", "showUptime": "false" }, "refreshSeconds": "30" },
                                                        { "kind": "uptime-kuma/monitor", "options": { "monitorId": "13" } },
                                                        { "id": 99, "keep": true }
                                                      ]
                                                      """,
                                                catalog: WidgetTests.Catalog,
                                                connections: [WidgetTests.KumaConnection (3)],
                                                existing: [saved],
                                                placement: WidgetPlacements.Tile,
                                                plan: out List<WidgetPlan>? plan,
                                                error: out string? error),
                 userMessage: error);

    Assert.Equal (expected:
                  [
                    new WidgetPlan (ExistingId: 7,
                                    ConnectionId: 3,
                                    Kind: "monitor",
                                    OptionsJson:
                                    """{"monitorId":12,"showResponseTime":false,"showUptime":false,"uptimeWindow":""}""",
                                    RefreshSeconds: 30),
                    new WidgetPlan (ExistingId: null,
                                    ConnectionId: 3,
                                    Kind: "monitor",
                                    OptionsJson:
                                    """{"monitorId":13,"showResponseTime":false,"showUptime":false,"uptimeWindow":""}""",
                                    RefreshSeconds: null),
                  ],
                  actual: plan);
  }

  [Theory]
  [InlineData ("""[{ "kind": "weather/now" }]""",                        "Widget 1: h2s has no such kind of widget.")]
  [InlineData ("""[{ "kind": "uptime-kuma/monitor", "options": {} }]""", "Widget 1 (Monitor status): Monitor is required.")]
  [InlineData ("""[{ "kind": "uptime-kuma/monitor", "options": { "monitorId": "1" }, "refreshSeconds": "5" }]""",
               "Widget 1 (Monitor status): the refresh interval must be a whole number of seconds, from 15 to 86400.")]
  [InlineData ("""[{ "kind": "app-test/app" }, { "kind": "app-test/app" }]""",
               "Widget 2 (App): a link can have only one app widget.")]
  [InlineData ("""not json""", "The widgets are malformed.")]
  public void WidgetForm_ExplainsWhatsWrong (string json, string expected)
  {
    Assert.False (WidgetForm.TryRead (json: json,
                                      catalog: WidgetTests.Catalog,
                                      connections:
                                      [
                                        WidgetTests.KumaConnection (3),
                                        new Connection { Id = 4, IntegrationKey = AppIntegration.IntegrationKey },
                                      ],
                                      existing: [],
                                      placement: WidgetPlacements.Tile,
                                      plan: out _,
                                      error: out string? error));
    Assert.Equal (expected: expected, actual: error);
  }

  [Fact]
  public void WidgetForm_NeedsTheIntegrationSetUp ()
  {
    Assert.False (WidgetForm.TryRead (json: """[{ "kind": "uptime-kuma/monitor", "options": { "monitorId": "1" } }]""",
                                      catalog: WidgetTests.Catalog,
                                      connections: [],
                                      existing: [],
                                      placement: WidgetPlacements.Tile,
                                      plan: out _,
                                      error: out string? error));
    Assert.Equal (expected: "Widget 1 (Monitor status): Uptime Kuma isn't set up; add it in Settings → Integrations.",
                  actual: error);
  }

  [Fact]
  public void WidgetForm_Apply_UpdatesAddsDeletesAndNumbers ()
  {
    var link = new Link
               {
                 Widgets =
                 [
                   new Widget { Id = 1, Kind = "monitor", SortOrder = 0 }, new Widget { Id = 2, Kind = "monitor", SortOrder = 1 },
                 ],
               };

    List<Widget> removed = WidgetForm.Apply (widgets: link.Widgets,
                                             plan:
                                             [
                                               new WidgetPlan (ExistingId: null,
                                                               ConnectionId: 3,
                                                               Kind: "monitor",
                                                               OptionsJson: "{}",
                                                               RefreshSeconds: null),
                                               new WidgetPlan (ExistingId: 2,
                                                               ConnectionId: 3,
                                                               Kind: "monitor",
                                                               OptionsJson: """{"x":1}""",
                                                               RefreshSeconds: 60),
                                             ]);

    Assert.Equal (expected: [(2, 1, """{"x":1}"""), (0, 0, "{}")],
                  actual: link.Widgets.Select (w => (w.Id, w.SortOrder, w.OptionsJson)));

    // Taking a widget out doesn't delete it: the caller does, since a widget's owner is optional in the model.
    Assert.Equal (expected: [1], actual: removed.Select (w => w.Id));
  }

  [Theory]
  [InlineData ("""[{ "kind": "app-test/app" }]""", "Widget 1 (App): this kind of widget can't go on a status bar.")]
  [InlineData ("""[{ "kind": "uptime-kuma/monitor", "options": { "monitorId": "1" }, "label": "LONG" }]""",
               "Widget 1 (Monitor status): the label can be at most 100 characters.")]
  public void WidgetForm_ChecksStatusBarItems (string json, string expected)
  {
    Assert.False (WidgetForm.TryRead (json: json.Replace (oldValue: "LONG", newValue: new string (c: 'x', count: 101)),
                                      catalog: WidgetTests.Catalog,
                                      connections:
                                      [
                                        WidgetTests.KumaConnection (3),
                                        new Connection { Id = 4, IntegrationKey = AppIntegration.IntegrationKey },
                                      ],
                                      existing: [],
                                      placement: WidgetPlacements.StatusBar,
                                      plan: out _,
                                      error: out string? error));
    Assert.Equal (expected: expected, actual: error);
  }

  [Fact]
  public void WidgetForm_ReadsLabels_AndAllowsMoreItemsOnAStatusBarThanOnATile ()
  {
    var json =
      $"[{string.Join (separator: ',', values: Enumerable.Repeat (element: """{ "kind": "uptime-kuma/monitor", "options": { "monitorId": "1" }, "label": " Compute " }""", count: 11))}]";

    Assert.True (WidgetForm.TryRead (json: json,
                                     catalog: WidgetTests.Catalog,
                                     connections: [WidgetTests.KumaConnection (3)],
                                     existing: [],
                                     placement: WidgetPlacements.StatusBar,
                                     plan: out List<WidgetPlan>? plan,
                                     error: out _));
    Assert.All (collection: plan, action: p => Assert.Equal (expected: "Compute", actual: p.Label));

    Assert.False (WidgetForm.TryRead (json: json,
                                      catalog: WidgetTests.Catalog,
                                      connections: [WidgetTests.KumaConnection (3)],
                                      existing: [],
                                      placement: WidgetPlacements.Tile,
                                      plan: out _,
                                      error: out string? error));
    Assert.Equal (expected: "A link can have at most 10 widgets.", actual: error);
  }

  [Fact]
  public void WidgetKinds_DefaultTheirPlacementsFromTheirRole ()
  {
    Assert.Equal (expected: WidgetPlacements.Tile | WidgetPlacements.StatusBar,
                  actual: ((IWidgetKind)new KumaMonitorWidgetKind ()).Placements);
    Assert.Equal (expected: WidgetPlacements.Tile, actual: ((IWidgetKind)new AppKind ()).Placements);
  }

  [Fact]
  public async Task WidgetFetcher_SharesOneRead_KeepsTheResult_AndTurnsFailuresIntoUnknown ()
  {
    using var cache   = new MemoryCache (new MemoryCacheOptions ());
    var       fetcher = new WidgetFetcher (cache: cache, logger: NullLogger<WidgetFetcher>.Instance);
    var       release = new TaskCompletionSource<WidgetResult> ();
    var       reads   = 0;

    Task<WidgetResult> Read (CancellationToken _)
    {
      Interlocked.Increment (ref reads);

      return release.Task;
    }

    Task<WidgetResult> first  = fetcher.GetAsync (key: "w", widgetId: 1, lifetime: TimeSpan.FromMinutes (1), fetch: Read);
    Task<WidgetResult> second = fetcher.GetAsync (key: "w", widgetId: 1, lifetime: TimeSpan.FromMinutes (1), fetch: Read);
    release.SetResult (new WidgetResult (Status: WidgetStatus.Ok, Stats: [], Description: "Fine."));

    Assert.Same (expected: await first, actual: await second);
    Assert.Equal (expected: 1,       actual: reads);
    Assert.Equal (expected: "Fine.", actual: fetcher.GetCached ("w")?.Description);

    WidgetResult failed = await fetcher.GetAsync (key: "broken",
                                                  widgetId: 2,
                                                  lifetime: TimeSpan.FromMinutes (1),
                                                  fetch: _ => throw new InvalidOperationException ("Boom"));

    Assert.Equal (expected: WidgetStatus.Unknown,                                            actual: failed.Status);
    Assert.Equal (expected: "This widget couldn't be read; the server log has the details.", actual: failed.Description);
  }

  /// <summary>
  ///   Reads a Kuma monitor widget against the stubbed Kuma.
  /// </summary>
  /// <param name="optionsJson">The widget's options.</param>
  /// <param name="apiKey">The connection's API key, if any.</param>
  /// <returns>The result.</returns>
  private static async Task<WidgetResult> FetchAsync (string optionsJson, string? apiKey)
  {
    using var cache = new MemoryCache (new MemoryCacheOptions ());
    var factory = new StubHttpClientFactory (new Dictionary<string, HttpMessageHandler>
                                             {
                                               [UptimeKumaClient.ClientName] =
                                                 new StubHttpMessageHandler (TestConnections.ServeKuma),
                                             });

    await using ServiceProvider services = new ServiceCollection ()
                                          .AddSingleton (new UptimeKumaClient (httpClientFactory: factory, cache: cache))
                                          .BuildServiceProvider ();

    var connection = new OptionValues (schema: new UptimeKumaIntegration ().ConnectionOptions,
                                       json: $$"""{"serverUrl":"{{TestConnections.KumaServerUrl}}","defaultUptimeWindow":"1d"}""",
                                       secrets: apiKey == null ? null : new Dictionary<string, string> { ["apiKey"] = apiKey });

    return await WidgetTests.Monitor.FetchAsync (new WidgetContext (Connection: connection,
                                                                    Options: new OptionValues (schema: WidgetTests.Monitor.Options,
                                                                                               json: optionsJson),
                                                                    Services: services,
                                                                    CancellationToken: TestContext.Current.CancellationToken));
  }

  /// <summary>
  ///   Creates an Uptime Kuma connection.
  /// </summary>
  /// <param name="id">Its ID.</param>
  /// <returns>The connection.</returns>
  private static Connection KumaConnection (int id) => new () { Id = id, IntegrationKey = UptimeKumaIntegration.IntegrationKey };

  /// <summary>
  ///   A test-only integration with an app widget kind.
  /// </summary>
  private sealed class AppIntegration : IIntegration
  {
    /// <summary>
    ///   The integration's key.
    /// </summary>
    public const string IntegrationKey = "app-test";

    /// <inheritdoc />
    public string Key => AppIntegration.IntegrationKey;

    /// <inheritdoc />
    public string DisplayName => "App test";

    /// <inheritdoc />
    public string Description => "An integration with an app widget, for tests.";

    /// <inheritdoc />
    public bool AllowsMultipleConnections => false;

    /// <inheritdoc />
    public IReadOnlyList<SchemaOption> ConnectionOptions => [];

    /// <inheritdoc />
    public IReadOnlyList<IWidgetKind> WidgetKinds { get; } = [new AppKind ()];
  }

  /// <summary>
  ///   A test-only app widget kind.
  /// </summary>
  private sealed class AppKind : IWidgetKind
  {
    /// <inheritdoc />
    public string Key => "app";

    /// <inheritdoc />
    public string DisplayName => "App";

    /// <inheritdoc />
    public WidgetRole Role => WidgetRole.App;

    /// <inheritdoc />
    public IReadOnlyList<SchemaOption> Options => [];

    /// <inheritdoc />
    public TimeSpan DefaultRefresh => TimeSpan.FromMinutes (1);

    /// <inheritdoc />
    public Task<WidgetResult> FetchAsync (WidgetContext context) => Task.FromResult (WidgetResult.Unknown ("Not really."));
  }
}

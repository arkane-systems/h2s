using System.Net;
using System.Text.Json;
using h2s.Integrations;
using h2s.Tests.Infrastructure;
using h2s.Widgets.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the link dialog's live choices (integrations' choice lists, offered by widget options) and its live preview
///   of the link's widgets, saved or not.
/// </summary>
public class LiveChoiceTests
{
  private const string EditorPath = "/Admin/Editor";

  private const string MonitorsQuery = "handler=Choices&integration=uptime-kuma&list=monitors";

  private const string JellyfinWidget =
    """[{ "kind": "uptime-kuma/monitor", "options": { "monitorId": "11", "showResponseTime": "true", "showUptime": "false" } }]""";

  [Fact]
  public async Task WidgetKinds_SayWhichListAnOptionOffersItsValuesFrom ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    JsonElement kind = Assert.Single ((await WidgetHandlerTests.GetJsonAsync (client: factory.CreateClient (),
                                                                              query: "handler=WidgetKinds")).EnumerateArray ());
    JsonElement monitor = kind.GetProperty ("options")
                              .EnumerateArray ()
                              .Single (o => o.GetProperty ("key").GetString () == "monitorId");

    Assert.Equal (expected: ("integer", "monitors"),
                  actual: (monitor.GetProperty ("type").GetString (), monitor.GetProperty ("choicesFrom").GetString ()));
  }

  [Fact]
  public async Task Choices_ListKumasMonitors_GroupsFirst ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    JsonElement list =
      await WidgetHandlerTests.GetJsonAsync (client: factory.CreateClient (), query: LiveChoiceTests.MonitorsQuery);

    Assert.Equal (expected: JsonValueKind.Null, actual: list.GetProperty ("message").ValueKind);
    Assert.Equal (expected:
                  [
                    ("1", "Infrastructure (#1)", "Groups"),
                    ("11", "Jellyfin (#11)", "Monitors"),
                    ("12", "Sonarr \"TV\" (#12)", "Monitors"),
                  ],
                  actual: list.GetProperty ("choices")
                              .EnumerateArray ()
                              .Select (c => (c.GetProperty ("value").GetString (), c.GetProperty ("label").GetString (),
                                             c.GetProperty ("group").GetString ())));
  }

  [Theory]
  [InlineData (false, null,            "Uptime Kuma isn't set up; add it in Settings → Integrations.")]
  [InlineData (true,  null,            "Add an API key to the Uptime Kuma settings to pick monitors.")]
  [InlineData (true,  "uk1_wrong-key", "Uptime Kuma refused the API key.")]
  public async Task Choices_SayWhyTheyCantBeListed (bool connected, string? apiKey, string message)
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };

    if (connected)
    {
      await factory.AddUptimeKumaAsync (apiKey: apiKey);
    }

    JsonElement list =
      await WidgetHandlerTests.GetJsonAsync (client: factory.CreateClient (), query: LiveChoiceTests.MonitorsQuery);

    Assert.Empty (list.GetProperty ("choices").EnumerateArray ());
    Assert.Equal (expected: message, actual: list.GetProperty ("message").GetString ());
  }

  [Fact]
  public async Task Choices_FromAnIntegrationThatFails_SayToCheckTheLog ()
  {
    await using var factory = new DashboardApplicationFactory
                              {
                                ConfigureServices = services => services.AddSingleton<IIntegration, FailingIntegration> (),
                              };
    HttpClient client = factory.CreateClient ();

    using (IServiceScope scope = factory.Services.CreateScope ())
    {
      _ = await scope.ServiceProvider.GetRequiredService<ConnectionService> ()
                     .SaveOnlyAsync (integration: new FailingIntegration (),
                                     optionsJson: "{}",
                                     cancellationToken: TestContext.Current.CancellationToken);
    }

    JsonElement list = await WidgetHandlerTests.GetJsonAsync (client: client,
                                                              query:
                                                              $"handler=Choices&integration={FailingIntegration.IntegrationKey}&list=things");

    Assert.Equal (expected: "The list couldn't be read; the server log has the details.",
                  actual: list.GetProperty ("message").GetString ());
  }

  [Theory]
  [InlineData ("handler=Choices&integration=nope&list=monitors")]
  [InlineData ("handler=Choices&integration=uptime-kuma&list=nope")]
  [InlineData ("handler=Choices")]
  public async Task Choices_ForNoSuchIntegrationOrList_AreNotFound (string query)
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    using HttpResponseMessage response = await factory.CreateClient ()
                                                      .GetAsync (requestUri: $"{LiveChoiceTests.EditorPath}?{query}",
                                                                 cancellationToken: TestContext.Current.CancellationToken);

    _ = await FormPoster.AssertProblemAsync (response: response,
                                             status: HttpStatusCode.NotFound,
                                             detail: "There's no such choice list.");
  }

  [Fact]
  public async Task TilePreview_ShowsUnsavedWidgets_WhichTheDialogReadsOneByOne ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    HttpClient client = factory.CreateClient ();

    string tile = await LiveChoiceTests.GetPreviewAsync (client: client, widgets: LiveChoiceTests.JellyfinWidget);

    // A placeholder the dialog reads by position, never one widgets.js would poll.
    Assert.Contains (expectedSubstring: """data-widget-preview="0" """, actualString: tile);
    Assert.Contains (expectedSubstring: "widget-pending",               actualString: tile);
    Assert.DoesNotContain (expectedSubstring: "data-widget-id", actualString: tile);

    using HttpResponseMessage widget =
      await client.GetAsync (requestUri: LiveChoiceTests.WidgetPreviewUri (widgets: LiveChoiceTests.JellyfinWidget, index: 0),
                             cancellationToken: TestContext.Current.CancellationToken);
    string html = await widget.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: widget.StatusCode);
    Assert.Contains (expectedSubstring: """data-widget-preview="0" """,                actualString: html);
    Assert.Contains (expectedSubstring: "link-widget link-widget-status widget-error", actualString: html);
    Assert.Contains (expectedSubstring: "Jellyfin is down: 42 ms.",                    actualString: html);

    // The next preview shows what was read.
    string again = await LiveChoiceTests.GetPreviewAsync (client: client, widgets: LiveChoiceTests.JellyfinWidget);

    Assert.DoesNotContain (expectedSubstring: "widget-pending", actualString: again);
    Assert.Contains (expectedSubstring: "42 ms", actualString: again);
  }

  [Fact]
  public async Task WidgetPreview_SharesWhatItReads_WithSavedWidgetsOfTheSameSettings ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    HttpClient client = factory.CreateClient ();

    using HttpResponseMessage widget =
      await client.GetAsync (requestUri: LiveChoiceTests.WidgetPreviewUri (widgets: LiveChoiceTests.JellyfinWidget, index: 0),
                             cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: widget.StatusCode);

    _ = await WidgetHandlerTests.CreateLinkAsync (client: client, factory: factory, widgets: LiveChoiceTests.JellyfinWidget);

    string page = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.DoesNotContain (expectedSubstring: "widget-pending", actualString: page);
    Assert.Contains (expectedSubstring: "42 ms", actualString: page);
  }

  [Fact]
  public async Task TilePreview_LeavesOutWidgetsThatDontCheckOut ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    string tile = await LiveChoiceTests.GetPreviewAsync (client: factory.CreateClient (),
                                                         widgets: """[{ "kind": "uptime-kuma/monitor", "options": {} }]""");

    Assert.DoesNotContain (expectedSubstring: "link-widget", actualString: tile);
    Assert.Contains (expectedSubstring: "Previewed", actualString: tile);
  }

  [Theory]
  [InlineData ("not json",                     0,  HttpStatusCode.BadRequest, "The widgets to preview don't check out.")]
  [InlineData (LiveChoiceTests.JellyfinWidget, 1,  HttpStatusCode.NotFound,   "There's no such widget to preview.")]
  [InlineData (LiveChoiceTests.JellyfinWidget, -1, HttpStatusCode.NotFound,   "There's no such widget to preview.")]
  public async Task WidgetPreview_RefusesWidgetsThatDontCheckOut (string widgets, int index, HttpStatusCode status, string detail)
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);

    using HttpResponseMessage response =
      await factory.CreateClient ()
                   .GetAsync (requestUri: LiveChoiceTests.WidgetPreviewUri (widgets: widgets, index: index),
                              cancellationToken: TestContext.Current.CancellationToken);

    _ = await FormPoster.AssertProblemAsync (response: response, status: status, detail: detail);
  }

  /// <summary>
  ///   Gets the link dialog's tile preview for a link with widgets.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="widgets">The widgets, as the dialog sends them.</param>
  /// <returns>The tile's HTML.</returns>
  private static async Task<string> GetPreviewAsync (HttpClient client, string widgets)
  {
    using HttpResponseMessage response =
      await client.GetAsync (requestUri: $"{LiveChoiceTests.EditorPath}?handler=TilePreview&label=Previewed&url=" +
                                         $"{Uri.EscapeDataString ("https://previewed.home.arpa/")}&widgets={Uri.EscapeDataString (widgets)}",
                             cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);
  }

  /// <summary>
  ///   Builds the address the dialog reads one preview widget from.
  /// </summary>
  /// <param name="widgets">The widgets the preview was rendered from.</param>
  /// <param name="index">The widget's position.</param>
  /// <returns>The address.</returns>
  private static string WidgetPreviewUri (string widgets, int index)
    => $"{LiveChoiceTests.EditorPath}?handler=WidgetPreview&widgets={Uri.EscapeDataString (widgets)}&index={index}";

  /// <summary>
  ///   An integration whose choice list throws, for tests.
  /// </summary>
  private sealed class FailingIntegration : IIntegration
  {
    /// <summary>
    ///   The integration's key.
    /// </summary>
    public const string IntegrationKey = "failing-test";

    /// <inheritdoc />
    public string Key => FailingIntegration.IntegrationKey;

    /// <inheritdoc />
    public string DisplayName => "Failing test";

    /// <inheritdoc />
    public string Description => "An integration whose choice list fails, for tests.";

    /// <inheritdoc />
    public bool AllowsMultipleConnections => false;

    /// <inheritdoc />
    public IReadOnlyList<SchemaOption> ConnectionOptions { get; } = [];

    /// <inheritdoc />
    public Task<ChoiceList?> GetChoicesAsync (string listKey, ConnectionContext context)
      => throw new InvalidOperationException ("The service broke.");
  }
}

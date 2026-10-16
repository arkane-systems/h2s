using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using h2s.Models;
using h2s.Panels;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the dashboard's panels: how the dashboard renders them, and the Editor handlers edit mode calls to add,
///   change, delete, restore and reorder them.
/// </summary>
public partial class PanelTests
{
  private const string EditorPath = "/Admin/Editor";

  [Fact]
  public async Task Index_RendersPanelsByZoneAndHidesEmptyOnes ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await PanelTests.UpdatePanelAsync (factory: factory,
                                       kind: SearchPanelKind.KindKey,
                                       zone: PanelZone.Bottom,
                                       optionsJson: """{"placeholder":"Find anything"}""");

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    // The status bar has nothing to show without Uptime Kuma, so the top zone is empty and isn't rendered.
    Assert.DoesNotContain (expectedSubstring: "panel-zone-top",    actualString: html);
    Assert.DoesNotContain (expectedSubstring: "uptime-status-bar", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "panel-slot",        actualString: html);
    Assert.Contains (expectedSubstring: """placeholder="Find anything""", actualString: html);
    Assert.True (html.IndexOf (value: "category-grid",     comparisonType: StringComparison.Ordinal) <
                 html.IndexOf (value: "panel-zone-bottom", comparisonType: StringComparison.Ordinal));
  }

  [Fact]
  public async Task Index_EditModeShowsEveryPanelWithAHandle ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/?edit=true",
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: ["Status bar", "Search"],
                  actual: PanelTests.PanelHandlePattern ().Matches (html).Select (m => m.Groups["name"].Value));
    Assert.Contains (expectedSubstring: "Uptime Kuma isn&#x27;t set up.", actualString: html);
    Assert.Contains (expectedSubstring: """data-zone="Top""",             actualString: html);
    Assert.Contains (expectedSubstring: """data-zone="Bottom""",          actualString: html);
  }

  [Fact]
  public async Task ReorderPanels_MovesAPanelToTheOtherZoneAndClosesTheGap ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await PanelTests.GetPanelIdsAsync (factory);

    HttpResponseMessage response = await PanelTests.PostAsync (factory: factory, zone: "bottom", orderedIds: [ids["status-bar"]]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: [("search", PanelZone.Top, 0), ("status-bar", PanelZone.Bottom, 0)],
                  actual: await PanelTests.GetPanelOrderAsync (factory));
  }

  [Fact]
  public async Task ReorderPanels_ReordersAZone ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await PanelTests.GetPanelIdsAsync (factory);

    HttpResponseMessage response =
      await PanelTests.PostAsync (factory: factory, zone: "Top", orderedIds: [ids["search"], ids["status-bar"]]);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: [("search", PanelZone.Top, 0), ("status-bar", PanelZone.Top, 1)],
                  actual: await PanelTests.GetPanelOrderAsync (factory));
  }

  [Fact]
  public async Task ReorderPanels_RequiresTheWholeZone ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await PanelTests.GetPanelIdsAsync (factory);

    HttpResponseMessage response = await PanelTests.PostAsync (factory: factory, zone: "Top", orderedIds: [ids["search"]]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "The order must include every panel in the zone.");
  }

  [Theory]
  [InlineData ("Middle")]
  [InlineData ("1")]
  [InlineData ("Top, Bottom")]
  [InlineData ("")]
  public async Task ReorderPanels_RejectsUnknownZones (string zone)
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await PanelTests.GetPanelIdsAsync (factory);

    HttpResponseMessage response =
      await PanelTests.PostAsync (factory: factory, zone: zone, orderedIds: [ids["status-bar"], ids["search"]]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "The zone must be Top or Bottom.");
  }

  [Fact]
  public async Task ReorderPanels_RejectsUnknownPanels ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await PanelTests.GetPanelIdsAsync (factory);

    HttpResponseMessage response =
      await PanelTests.PostAsync (factory: factory, zone: "Top", orderedIds: [ids["status-bar"], ids["search"], 999]);

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "One or more panels do not exist.");
  }

  [Fact]
  public async Task Index_EditModeOffersPanelEditing ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/?edit=true",
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """data-role="add-panel""",                               actualString: html);
    Assert.Contains (expectedSubstring: """aria-label="Edit panel Search""",                      actualString: html);
    Assert.Contains (expectedSubstring: """data-create-url="/Admin/Editor?handler=CreatePanel""", actualString: html);
    Assert.Matches (expectedRegexPattern: """<script type="module" src="/js/modules/edit-mode\.[^"]*js""", actualString: html);
  }

  [Fact]
  public async Task PanelKinds_MarksSingleInstanceKindsOnTheDashboardUnavailable ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    JsonElement kinds = await PanelTests.GetJsonAsync (client: client, query: "handler=PanelKinds");

    Assert.Equal (expected: [("status-bar", false), ("search", false)],
                  actual: kinds.EnumerateArray ()
                               .Select (k => (k.GetProperty ("key").GetString ()!, k.GetProperty ("isAvailable").GetBoolean ())));

    JsonElement statusBarOptions = kinds[0].GetProperty ("options");
    Assert.Equal (expected: ["showUptime", "uptimeWindow"],
                  actual: statusBarOptions.EnumerateArray ().Select (o => o.GetProperty ("key").GetString ()!));
    Assert.Equal (expected: "boolean", actual: statusBarOptions[0].GetProperty ("type").GetString ());
    Assert.Equal (expected: "select",  actual: statusBarOptions[1].GetProperty ("type").GetString ());
    Assert.Equal (expected: ["1d", "30d", "365d"],
                  actual: statusBarOptions[1]
                         .GetProperty ("choices")
                         .EnumerateArray ()
                         .Select (c => c.GetProperty ("value").GetString ()!));
  }

  [Fact]
  public async Task Panel_ReturnsItsSchemaAndValues ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await PanelTests.UpdatePanelAsync (factory: factory,
                                       kind: StatusBarPanelKind.KindKey,
                                       zone: PanelZone.Top,
                                       optionsJson: """{"uptimeWindow":"30d"}""");
    Dictionary<string, int> ids = await PanelTests.GetPanelIdsAsync (factory);

    JsonElement panel = await PanelTests.GetJsonAsync (client: factory.CreateClient (),
                                                       query: $"handler=Panel&id={ids["status-bar"]}");

    Assert.Equal (expected: "Status bar", actual: panel.GetProperty ("displayName").GetString ());
    Assert.True (panel.GetProperty ("isKnown").GetBoolean ());
    Assert.Equal (expected: "Top", actual: panel.GetProperty ("zone").GetString ());
    Assert.True (panel.GetProperty ("values").GetProperty ("showUptime").GetBoolean ());
    Assert.Equal (expected: "30d", actual: panel.GetProperty ("values").GetProperty ("uptimeWindow").GetString ());
  }

  [Fact]
  public async Task UpdatePanel_SavesValidOptions ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await PanelTests.GetPanelIdsAsync (factory);

    HttpResponseMessage response = await PanelTests.PostFieldsAsync (factory: factory,
                                                                     handler: "UpdatePanel",
                                                                     ("id", ids["search"].ToString ()),
                                                                     ("options[placeholder]", " Find it "));

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: """{"placeholder":"Find it"}""",
                  actual: await PanelTests.GetOptionsAsync (factory: factory, kind: "search"));

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);
    Assert.Contains (expectedSubstring: """placeholder="Find it""", actualString: html);
  }

  [Fact]
  public async Task UpdatePanel_RejectsInvalidOptions ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await PanelTests.GetPanelIdsAsync (factory);

    HttpResponseMessage response = await PanelTests.PostFieldsAsync (factory: factory,
                                                                     handler: "UpdatePanel",
                                                                     ("id", ids["status-bar"].ToString ()),
                                                                     ("options[showUptime]", "true"),
                                                                     ("options[uptimeWindow]", "7d"));

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "Uptime period must be one of 1 day, 30 days, 365 days.");
    Assert.Equal (expected: "{}", actual: await PanelTests.GetOptionsAsync (factory: factory, kind: "status-bar"));
  }

  [Fact]
  public async Task CreatePanel_RefusesASecondSingleInstancePanel ()
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response =
      await PanelTests.PostFieldsAsync (factory: factory, handler: "CreatePanel", ("kind", "search"), ("zone", "Bottom"));

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "The dashboard already has a search panel, and can only have one.");
  }

  [Theory]
  [InlineData ("weather", "Top",    "Choose a kind of panel to add.")]
  [InlineData ("",        "Top",    "Choose a kind of panel to add.")]
  [InlineData ("search",  "Middle", "The zone must be Top or Bottom.")]
  public async Task CreatePanel_RejectsBadKindsAndZones (string kind, string zone, string detail)
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpResponseMessage response =
      await PanelTests.PostFieldsAsync (factory: factory, handler: "CreatePanel", ("kind", kind), ("zone", zone));

    await FormPoster.AssertProblemAsync (response: response, status: HttpStatusCode.BadRequest, detail: detail);
  }

  [Fact]
  public async Task DeleteThenRestore_PutsThePanelBackWhereItWas ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await PanelTests.GetPanelIdsAsync (factory);
    await PanelTests.UpdatePanelAsync (factory: factory,
                                       kind: "search",
                                       zone: PanelZone.Top,
                                       optionsJson: """{"placeholder":"Go"}""");
    await PanelTests.PostAsync (factory: factory, zone: "Top", orderedIds: [ids["status-bar"], ids["search"]]);

    JsonElement deleted = await FormPoster.ReadJsonAsync (await PanelTests.PostFieldsAsync (factory: factory,
                                                                                            handler: "DeletePanel",
                                                                                            ("id", ids["status-bar"].ToString ())));

    Assert.Equal (expected: [("search", PanelZone.Top, 0)], actual: await PanelTests.GetPanelOrderAsync (factory));

    // With the status bar gone, it can be added again.
    JsonElement kinds = await PanelTests.GetJsonAsync (client: factory.CreateClient (), query: "handler=PanelKinds");
    Assert.True (kinds[0].GetProperty ("isAvailable").GetBoolean ());

    HttpResponseMessage restored = await PanelTests.PostFieldsAsync (factory: factory,
                                                                     handler: "RestorePanel",
                                                                     ("snapshot", deleted.GetProperty ("restore").GetRawText ()));
    JsonElement panel = await FormPoster.ReadJsonAsync (restored);

    Assert.Equal (expected: HttpStatusCode.OK, actual: restored.StatusCode);
    Assert.Equal (expected: 0,                 actual: panel.GetProperty ("sortOrder").GetInt32 ());
    Assert.Equal (expected: "Top",             actual: panel.GetProperty ("zone").GetString ());
    Assert.Equal (expected: [("status-bar", PanelZone.Top, 0), ("search", PanelZone.Top, 1)],
                  actual: await PanelTests.GetPanelOrderAsync (factory));

    // Restoring again would make a second status bar.
    HttpResponseMessage again = await PanelTests.PostFieldsAsync (factory: factory,
                                                                  handler: "RestorePanel",
                                                                  ("snapshot", deleted.GetProperty ("restore").GetRawText ()));
    await FormPoster.AssertProblemAsync (response: again,
                                         status: HttpStatusCode.BadRequest,
                                         detail: "The dashboard already has a status bar panel, and can only have one.");
  }

  [Fact]
  public async Task CreatePanel_AppendsToTheZoneAndRendersItsSlot ()
  {
    await using var         factory = new DashboardApplicationFactory ();
    Dictionary<string, int> ids     = await PanelTests.GetPanelIdsAsync (factory);
    await PanelTests.PostFieldsAsync (factory: factory, handler: "DeletePanel", ("id", ids["search"].ToString ()));

    HttpResponseMessage response = await PanelTests.PostFieldsAsync (factory: factory,
                                                                     handler: "CreatePanel",
                                                                     ("kind", "search"),
                                                                     ("zone", "bottom"),
                                                                     ("options[placeholder]", "Look it up"));
    JsonElement panel = await FormPoster.ReadJsonAsync (response);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: "Bottom",          actual: panel.GetProperty ("zone").GetString ());
    Assert.Equal (expected: [("status-bar", PanelZone.Top, 0), ("search", PanelZone.Bottom, 0)],
                  actual: await PanelTests.GetPanelOrderAsync (factory));

    string slot = await factory.CreateClient ()
                               .GetStringAsync (requestUri:
                                                $"{PanelTests.EditorPath}?handler=PanelSlot&id={panel.GetProperty ("id").GetInt32 ()}",
                                                cancellationToken: TestContext.Current.CancellationToken);
    Assert.StartsWith (expectedStartString: """<div class="panel-slot" """, actualString: slot.TrimStart ('\uFEFF').Trim ());
    Assert.Contains (expectedSubstring: """placeholder="Look it up""", actualString: slot);
  }

  [Fact]
  public async Task UpdatePanel_RefusesPanelsOfUnknownKinds ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context) = factory.CreateContext ();
    int id;

    using (scope)
    {
      var panel = new Panel { Kind = "weather", Zone = PanelZone.Bottom, SortOrder = 0 };
      context.Panels.Add (panel);
      await context.SaveChangesAsync (TestContext.Current.CancellationToken);
      id = panel.Id;
    }

    JsonElement details = await PanelTests.GetJsonAsync (client: factory.CreateClient (), query: $"handler=Panel&id={id}");
    Assert.False (details.GetProperty ("isKnown").GetBoolean ());

    HttpResponseMessage response =
      await PanelTests.PostFieldsAsync (factory: factory, handler: "UpdatePanel", ("id", id.ToString ()));

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail:
                                         "This version of h2s doesn't know this panel's kind, so it can't change its options.");
  }

  /// <summary>
  ///   Gets JSON from one of the Editor's GET handlers.
  /// </summary>
  /// <param name="client">The client.</param>
  /// <param name="query">The query string, without the <c>?</c>.</param>
  /// <returns>The JSON.</returns>
  private static async Task<JsonElement> GetJsonAsync (HttpClient client, string query)
  {
    using HttpResponseMessage response = await client.GetAsync (requestUri: $"{PanelTests.EditorPath}?{query}",
                                                                cancellationToken: TestContext.Current.CancellationToken);

    return await FormPoster.ReadJsonAsync (response);
  }

  /// <summary>
  ///   Gets the stored options of the panel of a kind.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="kind">The panel's kind key.</param>
  /// <returns>The options JSON.</returns>
  private static async Task<string> GetOptionsAsync (DashboardApplicationFactory factory, string kind)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return (await context.Panels.SingleAsync (predicate: p => p.Kind == kind,
                                                cancellationToken: TestContext.Current.CancellationToken)).OptionsJson;
    }
  }

  /// <summary>
  ///   Posts form fields to an Editor handler.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="handler">The handler name.</param>
  /// <param name="fields">The fields.</param>
  /// <returns>The response.</returns>
  private static Task<HttpResponseMessage> PostFieldsAsync (DashboardApplicationFactory          factory,
                                                            string                               handler,
                                                            params (string Name, string Value)[] fields)
    => FormPoster.PostAsync (client: factory.CreateClient (),
                             pagePath: PanelTests.EditorPath,
                             handler: handler,
                             fields: [.. fields.Select (f => new KeyValuePair<string, string> (key: f.Name, value: f.Value))]);

  /// <summary>
  ///   Changes a panel of the given kind.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="kind">The panel's kind key.</param>
  /// <param name="zone">The panel's new zone.</param>
  /// <param name="optionsJson">The panel's new options.</param>
  private static async Task UpdatePanelAsync (DashboardApplicationFactory factory, string kind, PanelZone zone, string optionsJson)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      await context.Panels.Where (p => p.Kind == kind)
                   .ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression: p => p.Zone,
                                                                              valueExpression: zone)
                                                                .SetProperty (propertyExpression: p => p.SortOrder,
                                                                              valueExpression: 0)
                                                                .SetProperty (propertyExpression: p => p.OptionsJson,
                                                                              valueExpression: optionsJson),
                                        cancellationToken: TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Posts a zone's ordered panel IDs to the reorder handler.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="zone">The zone.</param>
  /// <param name="orderedIds">The panel IDs, in order.</param>
  /// <returns>The response.</returns>
  private static Task<HttpResponseMessage> PostAsync (DashboardApplicationFactory factory, string zone, int[] orderedIds)
    => FormPoster.PostAsync (client: factory.CreateClient (),
                             pagePath: PanelTests.EditorPath,
                             handler: "ReorderPanels",
                             fields:
                             [
                               new KeyValuePair<string, string> (key: "zone", value: zone),
                               .. orderedIds.Select (id => new KeyValuePair<string, string> (key: "orderedIds",
                                                                                             value: id.ToString ())),
                             ]);

  /// <summary>
  ///   Gets the ID of every panel, by kind.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The panel IDs.</returns>
  private static async Task<Dictionary<string, int>> GetPanelIdsAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.Panels.ToDictionaryAsync (keySelector: p => p.Kind,
                                                     elementSelector: p => p.Id,
                                                     cancellationToken: TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Gets every panel's kind, zone and position, in dashboard order.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The panels.</returns>
  private static async Task<List<(string Kind, PanelZone Zone, int SortOrder)>> GetPanelOrderAsync (
    DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return (await context.Panels.OrderBy (p => p.Zone)
                           .ThenBy (p => p.SortOrder)
                           .ToListAsync (TestContext.Current.CancellationToken))
            .Select (p => (p.Kind, p.Zone, p.SortOrder))
            .ToList ();
    }
  }

  [GeneratedRegex ("""aria-label="Move panel (?<name>[^"]+)">""")]
  private static partial Regex PanelHandlePattern ();
}

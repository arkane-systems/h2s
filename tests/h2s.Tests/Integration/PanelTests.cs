using System.Net;
using System.Text.RegularExpressions;
using h2s.Models;
using h2s.Panels;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the dashboard's panels: how the dashboard renders them, and the reorder handler edit mode calls.
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

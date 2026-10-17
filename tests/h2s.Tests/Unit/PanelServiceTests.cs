using h2s.Data;
using h2s.Integrations;
using h2s.Models;
using h2s.Panels;
using h2s.Services;
using h2s.Tests.Infrastructure;
using h2s.Widgets.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="PanelService" /> and the built-in panel kinds.
/// </summary>
public class PanelServiceTests
{
  [Fact]
  public async Task GetPanelsAsync_ReturnsTheDefaultPanelsInOrder ()
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();

    List<PanelView> panels = await PanelServiceTests.GetPanelsAsync (context);

    Assert.Equal (expected: [("Status bar", PanelZone.Top), ("Search", PanelZone.Top)],
                  actual: panels.Select (p => (p.DisplayName, p.Zone)));

    // Seeded data has no status bar items.
    Assert.True (panels[0].Content.IsEmpty);
    Assert.Equal (expected: "No items yet. Add status widgets with the panel's ✎.", actual: panels[0].Content.EmptyMessage);
    Assert.Equal (expected: "_SearchPanel",                                         actual: panels[1].Content.PartialName);
    var search = (SearchPanelModel)panels[1].Content.Model!;
    Assert.Equal (expected: "Search with Bing", actual: search.Placeholder);
    Assert.Equal (expected: "Bing",             actual: search.EngineName);
    Assert.Equal (expected: [("Bing", "b", true), ("DuckDuckGo", "d", false), ("Google", "g", false)],
                  actual: search.Engines.Select (e => (e.Name, e.Shortcut, e.IsDefault)));
  }

  [Theory]
  [InlineData ("{}", null, null)]
  [InlineData ("""{ "title": "Home", "linkUrl": "https://status.home.arpa/status/main" }""",
               "Home",
               "https://status.home.arpa/status/main")]
  public async Task StatusBar_ShowsItsWidgets_WithItsTitleAndLink (string optionsJson, string? title, string? link)
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();

    Connection kuma = TestConnections.UptimeKuma ();
    Panel statusBar = await context.Panels.SingleAsync (predicate: p => p.Kind == StatusBarPanelKind.KindKey,
                                                        cancellationToken: TestContext.Current.CancellationToken);

    statusBar.OptionsJson = optionsJson;
    statusBar.Widgets.AddRange (new Widget
                                {
                                  Connection = kuma, Kind = "monitor", OptionsJson = """{"monitorId":2}""", SortOrder = 1,
                                },
                                new Widget
                                {
                                  Connection  = kuma,
                                  Kind        = "monitor",
                                  OptionsJson = """{"monitorId":1}""",
                                  Label       = "Compute",
                                  SortOrder   = 0,
                                });
    await context.SaveChangesAsync (TestContext.Current.CancellationToken);

    var model = (StatusBarPanelModel)(await PanelServiceTests.GetPanelsAsync (context))[0].Content.Model!;

    Assert.Equal (expected: (title, link),     actual: (model.Title, model.LinkUrl));
    Assert.Equal (expected: ["Compute", null], actual: model.Items.Select (i => i.Label));

    // Not read yet, so the page reads them.
    Assert.All (collection: model.Items, action: i => Assert.Null (i.Result));
  }

  [Fact]
  public async Task GetPanelsAsync_ShowsUnknownAndFailingKindsAsEmpty ()
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();

    context.Panels.AddRange (new Panel { Kind = "weather", Zone           = PanelZone.Bottom, SortOrder = 0 },
                             new Panel { Kind = FailingKind.KindKey, Zone = PanelZone.Bottom, SortOrder = 1 });
    await context.SaveChangesAsync (TestContext.Current.CancellationToken);

    List<PanelView> panels = await PanelServiceTests.GetPanelsAsync (context: context, extraKind: new FailingKind ());

    Assert.Equal (expected: ["Status bar", "Search", "weather", "Failing"], actual: panels.Select (p => p.DisplayName));
    Assert.Equal (expected: "This version of h2s doesn't know this panel's kind (\"weather\").",
                  actual: panels[2].Content.EmptyMessage);
    Assert.Equal (expected: "This panel couldn't be shown; the server log has the details.",
                  actual: panels[3].Content.EmptyMessage);
  }

  [Fact]
  public async Task Search_IsEmptyWithoutEngines ()
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();
    await context.SearchEngines.ExecuteDeleteAsync (TestContext.Current.CancellationToken);

    PanelView search = (await PanelServiceTests.GetPanelsAsync (context))[1];

    Assert.Equal (expected: "There are no search engines. Add one in Settings → Search.", actual: search.Content.EmptyMessage);
  }

  [Fact]
  public async Task Search_NamesTheDefaultEngineUnlessGivenAPlaceholder ()
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();
    await context.SearchEngines.ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression: e => e.IsDefault,
                                                                                          valueExpression: e => e.Name == "Google"),
                                                    cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: "Search with Google",
                  actual: ((SearchPanelModel)(await PanelServiceTests.GetPanelsAsync (context))[1].Content.Model!).Placeholder);

    await context.Panels.Where (p => p.Kind == SearchPanelKind.KindKey)
                 .ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression: p => p.OptionsJson,
                                                                            valueExpression: """{"placeholder":"Ask the web"}"""),
                                      cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: "Ask the web",
                  actual: ((SearchPanelModel)(await PanelServiceTests.GetPanelsAsync (context))[1].Content.Model!).Placeholder);
  }

  [Fact]
  public void PanelCatalog_RejectsDuplicateKeys ()
    => Assert.Throws<ArgumentException> (() => new PanelCatalog ([new SearchPanelKind (), new SearchPanelKind ()]));

  /// <summary>
  ///   Loads the panels through a <see cref="PanelService" /> with the built-in kinds.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <param name="extraKind">Another kind to register, if any.</param>
  /// <returns>The panels.</returns>
  private static async Task<List<PanelView>> GetPanelsAsync (DashboardContext context, IPanelKind? extraKind = null)
  {
    using var cache = new MemoryCache (new MemoryCacheOptions ());

    await using ServiceProvider services = new ServiceCollection ().AddSingleton (context)
                                                                   .AddSingleton (new DashboardSettingsService (context: context,
                                                                                                                cache: cache))
                                                                   .AddSingleton (TestConnections.CreateService (context: context,
                                                                                                                 cache: cache))
                                                                   .AddSingleton (provider => TestConnections
                                                                                   .CreateWidgetService (context: context,
                                                                                                         connections: provider
                                                                                                          .GetRequiredService<
                                                                                                             ConnectionService> (),
                                                                                                         cache: cache,
                                                                                                         services: provider))
                                                                   .BuildServiceProvider ();

    IPanelKind[] kinds = [new StatusBarPanelKind (), new SearchPanelKind (), .. extraKind is null ? [] : new[] { extraKind }];

    return await new PanelService (context: context,
                                   catalog: new PanelCatalog (kinds),
                                   services: services,
                                   logger: NullLogger<PanelService>.Instance)
            .GetPanelsAsync (TestContext.Current.CancellationToken);
  }

  /// <summary>
  ///   A panel kind that always fails.
  /// </summary>
  private sealed class FailingKind : IPanelKind
  {
    /// <summary>
    ///   The kind's key.
    /// </summary>
    public const string KindKey = "failing";

    /// <inheritdoc />
    public string Key => FailingKind.KindKey;

    /// <inheritdoc />
    public string DisplayName => "Failing";

    /// <inheritdoc />
    public bool IsSingleInstance => false;

    /// <inheritdoc />
    public IReadOnlyList<SchemaOption> Options => [];

    /// <inheritdoc />
    public Task<PanelContent> GetContentAsync (PanelContext context) => throw new InvalidOperationException ("Broken.");
  }
}

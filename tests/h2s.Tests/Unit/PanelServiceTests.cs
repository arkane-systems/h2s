using h2s.Data;
using h2s.Models;
using h2s.Panels;
using h2s.Services;
using h2s.Tests.Infrastructure;
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

    // Seeded data leaves Uptime Kuma unconfigured.
    Assert.True (panels[0].Content.IsEmpty);
    Assert.Equal (expected: "Uptime Kuma isn't set up. Add its server in Settings → Integrations.",
                  actual: panels[0].Content.EmptyMessage);
    Assert.Equal (expected: "_SearchPanel", actual: panels[1].Content.PartialName);
    var search = (SearchPanelModel)panels[1].Content.Model!;
    Assert.Equal (expected: "Search with Bing",                        actual: search.Placeholder);
    Assert.Equal (expected: "Bing",                                    actual: search.EngineName);
    Assert.Equal (expected: ["!b Bing", "!d DuckDuckGo", "!g Google"], actual: search.Shortcuts);
  }

  [Fact]
  public async Task StatusBar_IsEmptyWithoutInfrastructureGroups ()
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();
    await PanelServiceTests.ConfigureKumaAsync (context: context, slug: null);

    PanelView statusBar = (await PanelServiceTests.GetPanelsAsync (context))[0];

    Assert.True (statusBar.Content.IsEmpty);
    Assert.StartsWith (expectedStartString: "No infrastructure groups yet.", actualString: statusBar.Content.EmptyMessage);
  }

  [Theory]
  [InlineData ("{}",                                           "main", "https://status.home.arpa/status/main", 24)]
  [InlineData ("""{ "uptimeHours": 168 }""",                   null,   null,                                   168)]
  [InlineData ("""{ "showUptime": false }""",                  "main", "https://status.home.arpa/status/main", null)]
  [InlineData ("""{ "showUptime": true, "uptimeHours": 0 }""", null,   null,                                   24)]
  public async Task StatusBar_AppliesItsOptions (string optionsJson, string? slug, string? statusPageUrl, int? uptimeHours)
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();
    await PanelServiceTests.ConfigureKumaAsync (context: context, slug: slug);
    await context.Panels.Where (p => p.Kind == StatusBarPanelKind.KindKey)
                 .ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression: p => p.OptionsJson,
                                                                            valueExpression: optionsJson),
                                      cancellationToken: TestContext.Current.CancellationToken);

    var model = (StatusBarPanelModel)(await PanelServiceTests.GetPanelsAsync (context))[0].Content.Model!;

    Assert.Equal (expected: statusPageUrl,                                            actual: model.StatusPageUrl);
    Assert.Equal (expected: ["Compute", "Storage", "Network"],                        actual: model.Items.Select (i => i.Name));
    Assert.Equal (expected: "https://status.home.arpa/api/badge/1/status?style=flat", actual: model.Items[0].StatusBadgeUrl);
    Assert.Equal (expected: uptimeHours is { } hours ? $"https://status.home.arpa/api/badge/1/uptime/{hours}?style=flat" : null,
                  actual: model.Items[0].UptimeBadgeUrl);
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
  ///   Configures Uptime Kuma with a server URL and, optionally, a status page slug.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <param name="slug">The status page slug, or <c>null</c> for none.</param>
  private static Task ConfigureKumaAsync (DashboardContext context, string? slug)
    => context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls: s => s
                                                                           .SetProperty (propertyExpression:
                                                                                         x => x.UptimeKumaServerUrl,
                                                                                         valueExpression:
                                                                                         "https://status.home.arpa")
                                                                           .SetProperty (propertyExpression:
                                                                                         x => x.UptimeKumaStatusPageSlug,
                                                                                         valueExpression: slug),
                                                     cancellationToken: TestContext.Current.CancellationToken);

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
    public IReadOnlyList<PanelOption> Options => [];

    /// <inheritdoc />
    public Task<PanelContent> GetContentAsync (PanelContext context) => throw new InvalidOperationException ("Broken.");
  }
}

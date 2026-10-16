using System.Text.Json;
using h2s.Data;
using h2s.Integrations;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Panels;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="DashboardBackupService" />.
/// </summary>
public class DashboardBackupServiceTests
{
  /// <summary>
  ///   The panel kinds h2s registers.
  /// </summary>
  private static readonly PanelCatalog Kinds = new ([new StatusBarPanelKind (), new SearchPanelKind ()]);

  /// <summary>
  ///   The integrations h2s registers.
  /// </summary>
  private static readonly IntegrationCatalog Integrations = new ([new UptimeKumaIntegration ()]);

  [Fact]
  public async Task ExportAsync_WritesTheConfigurationInDashboardOrder ()
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();

    DashboardBackup backup = await DashboardBackupServiceTests.CreateService (context)
                                                              .ExportAsync (TestContext.Current.CancellationToken);

    Assert.Equal (expected: DashboardBackup.FormatName,     actual: backup.Format);
    Assert.Equal (expected: DashboardBackup.CurrentVersion, actual: backup.Version);
    Assert.Equal (expected: "Development",                  actual: backup.Settings!.Title);
    Assert.Equal (expected: ["Media", "Home", "Reference", "Infrastructure", "Network"],
                  actual: backup.Categories!.Select (c => c.Name));
    Assert.Equal (expected: ["Jellyfin", "Sonarr", "Radarr", "Audiobookshelf"],
                  actual: backup.Categories![0].Links!.Select (l => l.Label));
    Assert.Equal (expected: ["Compute", "Storage", "Network"], actual: backup.InfrastructureGroups!.Select (g => g.Name));
    Assert.Equal (expected: [(StatusBarPanelKind.KindKey, PanelZone.Top), (SearchPanelKind.KindKey, PanelZone.Top)],
                  actual: backup.Panels!.Select (p => (p.Kind, p.Zone)));
    Assert.Equal (expected: "{}", actual: backup.Panels![0].Options!.Value.GetRawText ());
  }

  [Fact]
  public async Task ExportThenImport_ReproducesThePanels ()
  {
    await using var source = await TestDatabase.CreateAsync (seed: true);
    await using var target = await TestDatabase.CreateAsync ();
    string          json;

    await using (DashboardContext context = source.CreateContext ())
    {
      Panel search = await context.Panels.SingleAsync (predicate: p => p.Kind == SearchPanelKind.KindKey,
                                                       cancellationToken: TestContext.Current.CancellationToken);
      search.Zone        = PanelZone.Bottom;
      search.SortOrder   = 0;
      search.OptionsJson = """{"placeholder":"Find it"}""";
      await context.SaveChangesAsync (TestContext.Current.CancellationToken);

      json = JsonSerializer.Serialize (value: await DashboardBackupServiceTests.CreateService (context)
                                                                               .ExportAsync (TestContext.Current.CancellationToken),
                                       options: DashboardBackup.JsonOptions);
    }

    Assert.True (DashboardBackupService.TryRead (json: json,
                                                 kinds: DashboardBackupServiceTests.Kinds,
                                                 integrations: DashboardBackupServiceTests.Integrations,
                                                 backup: out DashboardBackup? backup,
                                                 errors: out _));

    await using DashboardContext targetContext = target.CreateContext ();
    await DashboardBackupServiceTests.CreateService (targetContext)
                                     .ImportAsync (backup: backup!, cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected:
                  [
                    (StatusBarPanelKind.KindKey, PanelZone.Top, 0, "{}"),
                    (SearchPanelKind.KindKey, PanelZone.Bottom, 0, """{"placeholder":"Find it"}"""),
                  ],
                  actual: await DashboardBackupServiceTests.DescribePanelsAsync (targetContext));
  }

  [Fact]
  public async Task ImportAsync_GivesAFileFromBeforePanelsTheDefaultPanels ()
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();

    // Rearranged first, so the import visibly puts the defaults back.
    await context.Panels.ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression: p => p.Zone,
                                                                                   valueExpression: PanelZone.Bottom)
                                                                     .SetProperty (propertyExpression: p => p.OptionsJson,
                                                                                   valueExpression: """{"showUptime":false}"""),
                                             cancellationToken: TestContext.Current.CancellationToken);

    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 1,
                          "settings": { "title": "Old", "colorMode": "Auto", "uptimeKumaDefaultDuration": 24 },
                          "categories": [],
                          "panels": [ { "kind": "nonsense", "zone": "Bottom" } ]
                        }
                        """;

    Assert.True (DashboardBackupService.TryRead (json: Json,
                                                 kinds: DashboardBackupServiceTests.Kinds,
                                                 integrations: DashboardBackupServiceTests.Integrations,
                                                 backup: out DashboardBackup? backup,
                                                 errors: out _));
    Assert.Null (backup!.Panels);
    Assert.Equal (expected: 2, actual: DashboardBackupService.Summarize (backup).Panels);

    await DashboardBackupServiceTests.CreateService (context)
                                     .ImportAsync (backup: backup, cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected:
                  [
                    (StatusBarPanelKind.KindKey, PanelZone.Top, 0, "{}"), (SearchPanelKind.KindKey, PanelZone.Top, 1, "{}"),
                  ],
                  actual: await DashboardBackupServiceTests.DescribePanelsAsync (context));
  }

  [Fact]
  public void TryRead_ChecksPanels ()
  {
    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 2,
                          "settings": { "title": "Home", "colorMode": "Auto", "uptimeKumaDefaultDuration": 24 },
                          "categories": [],
                          "panels": [
                            { "kind": "search", "zone": "Top" },
                            { "kind": "search", "zone": "Bottom" },
                            { "kind": "weather", "zone": "Top" },
                            { "kind": " ", "zone": "Top" },
                            { "kind": "status-bar", "zone": "Top", "options": [1, 2] }
                          ]
                        }
                        """;

    Assert.False (DashboardBackupService.TryRead (json: Json,
                                                  kinds: DashboardBackupServiceTests.Kinds,
                                                  integrations: DashboardBackupServiceTests.Integrations,
                                                  backup: out _,
                                                  errors: out List<string> errors));
    Assert.Equal (expected:
                  [
                    "Panel 2: the dashboard can only have one search panel.",
                    "Panel 3: h2s has no “weather” panel kind.",
                    "Panel 4: the kind is required.",
                    "Panel 5: the options must be a JSON object.",
                  ],
                  actual: errors);
  }

  [Fact]
  public async Task ExportThenImport_ReproducesTheSearchEngines ()
  {
    await using var source = await TestDatabase.CreateAsync (seed: true);
    await using var target = await TestDatabase.CreateAsync ();
    string          json;

    await using (DashboardContext context = source.CreateContext ())
    {
      context.SearchEngines.Add (new SearchEngine
                                 {
                                   Name        = "Wikipedia",
                                   UrlTemplate = "https://en.wikipedia.org/w/index.php?search={query}",
                                   Shortcut    = "w",
                                   IsDefault   = true,
                                   SortOrder   = 3,
                                 });
      await context.SearchEngines.Where (e => e.Name == "Bing")
                   .ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression: e => e.IsDefault,
                                                                              valueExpression: false),
                                        cancellationToken: TestContext.Current.CancellationToken);
      await context.SaveChangesAsync (TestContext.Current.CancellationToken);

      json = JsonSerializer.Serialize (value: await DashboardBackupServiceTests.CreateService (context)
                                                                               .ExportAsync (TestContext.Current.CancellationToken),
                                       options: DashboardBackup.JsonOptions);
    }

    Assert.True (DashboardBackupService.TryRead (json: json,
                                                 kinds: DashboardBackupServiceTests.Kinds,
                                                 integrations: DashboardBackupServiceTests.Integrations,
                                                 backup: out DashboardBackup? backup,
                                                 errors: out _));

    await using DashboardContext targetContext = target.CreateContext ();
    await DashboardBackupServiceTests.CreateService (targetContext)
                                     .ImportAsync (backup: backup!, cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected:
                  [
                    ("Bing", "b", false, 0), ("DuckDuckGo", "d", false, 1), ("Google", "g", false, 2), ("Wikipedia", "w", true, 3),
                  ],
                  actual: await DashboardBackupServiceTests.DescribeEnginesAsync (targetContext));
  }

  [Fact]
  public async Task ImportAsync_GivesAFileFromBeforeSearchEnginesTheDefaultEngines ()
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();
    await context.SearchEngines.ExecuteDeleteAsync (TestContext.Current.CancellationToken);

    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 2,
                          "settings": { "title": "Old", "colorMode": "Auto", "uptimeKumaDefaultDuration": 24 },
                          "categories": [], "panels": [],
                          "searchEngines": [ { "name": "Ignored", "urlTemplate": "nonsense" } ]
                        }
                        """;

    Assert.True (DashboardBackupService.TryRead (json: Json,
                                                 kinds: DashboardBackupServiceTests.Kinds,
                                                 integrations: DashboardBackupServiceTests.Integrations,
                                                 backup: out DashboardBackup? backup,
                                                 errors: out _));
    Assert.Null (backup!.SearchEngines);

    await DashboardBackupServiceTests.CreateService (context)
                                     .ImportAsync (backup: backup, cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: [("Bing", "b", true, 0), ("DuckDuckGo", "d", false, 1), ("Google", "g", false, 2)],
                  actual: await DashboardBackupServiceTests.DescribeEnginesAsync (context));
  }

  [Fact]
  public async Task ExportThenImport_KeepsWhichLinksOpenInANewTab ()
  {
    await using var source = await TestDatabase.CreateAsync (seed: true);
    await using var target = await TestDatabase.CreateAsync ();
    string          json;

    await using (DashboardContext context = source.CreateContext ())
    {
      json = JsonSerializer.Serialize (value: await DashboardBackupServiceTests.CreateService (context)
                                                                               .ExportAsync (TestContext.Current.CancellationToken),
                                       options: DashboardBackup.JsonOptions);
    }

    Assert.True (DashboardBackupService.TryRead (json: json,
                                                 kinds: DashboardBackupServiceTests.Kinds,
                                                 integrations: DashboardBackupServiceTests.Integrations,
                                                 backup: out DashboardBackup? backup,
                                                 errors: out _));

    await using DashboardContext targetContext = target.CreateContext ();
    await DashboardBackupServiceTests.CreateService (targetContext)
                                     .ImportAsync (backup: backup!, cancellationToken: TestContext.Current.CancellationToken);

    // The seeder opens only GitHub in a new tab.
    Assert.Equal (expected: ["GitHub"],
                  actual: await targetContext.Links.Where (l => l.OpenInNewTab)
                                             .Select (l => l.Label)
                                             .ToListAsync (TestContext.Current.CancellationToken));
  }

  [Fact]
  public void TryRead_LinksFromBeforeVersion4OpenInTheSameTab ()
  {
    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 3,
                          "settings": { "title": "Home", "colorMode": "Auto", "uptimeKumaDefaultDuration": 24 },
                          "categories": [ { "name": "A", "links": [ { "label": "One", "url": "https://one.home.arpa/" } ] } ],
                          "panels": [], "searchEngines": []
                        }
                        """;

    Assert.True (DashboardBackupService.TryRead (json: Json,
                                                 kinds: DashboardBackupServiceTests.Kinds,
                                                 integrations: DashboardBackupServiceTests.Integrations,
                                                 backup: out DashboardBackup? backup,
                                                 errors: out _));
    Assert.False (backup!.Categories![0].Links![0].OpenInNewTab);
  }

  [Fact]
  public void TryRead_ChecksSearchEngines ()
  {
    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 3,
                          "settings": { "title": "Home", "colorMode": "Auto", "uptimeKumaDefaultDuration": 24 },
                          "categories": [], "panels": [],
                          "searchEngines": [
                            { "name": "A", "urlTemplate": "https://a.example/?q={query}", "shortcut": "!A", "isDefault": true },
                            { "name": "B", "urlTemplate": "https://b.example/?q={query}", "shortcut": "a", "isDefault": true },
                            { "name": "", "urlTemplate": "https://c.example/?q=" },
                            { "name": "D", "urlTemplate": "ftp://d.example/{query}", "shortcut": "no spaces" }
                          ]
                        }
                        """;

    Assert.False (DashboardBackupService.TryRead (json: Json,
                                                  kinds: DashboardBackupServiceTests.Kinds,
                                                  integrations: DashboardBackupServiceTests.Integrations,
                                                  backup: out _,
                                                  errors: out List<string> errors));
    Assert.Equal (expected:
                  [
                    "Search engine 2 (\"B\"): another engine already uses !a.",
                    "Search engine 3: a name is required.",
                    "Search engine 3: the search URL must contain {query} where the search terms go.",
                    "Search engine 4 (\"D\"): the search URL must start with http:// or https://.",
                    "Search engine 4 (\"D\"): the shortcut may only use letters, numbers and hyphens, at most 20 of them.",
                    "Only one search engine can be the default.",
                  ],
                  actual: errors);
  }

  [Fact]
  public void TryRead_RequiresPanelsFromVersion2 ()
  {
    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 2,
                          "settings": { "title": "Home", "colorMode": "Auto", "uptimeKumaDefaultDuration": 24 },
                          "categories": []
                        }
                        """;

    Assert.False (DashboardBackupService.TryRead (json: Json,
                                                  kinds: DashboardBackupServiceTests.Kinds,
                                                  integrations: DashboardBackupServiceTests.Integrations,
                                                  backup: out _,
                                                  errors: out List<string> errors));
    Assert.Equal (expected: ["The file has no panels list."], actual: errors);
  }

  [Fact]
  public async Task ExportThenImport_ReproducesTheConfiguration ()
  {
    await using var source = await TestDatabase.CreateAsync (seed: true);
    await using var target = await TestDatabase.CreateAsync ();
    string          json;

    await using (DashboardContext context = source.CreateContext ())
    {
      json = JsonSerializer.Serialize (value: await DashboardBackupServiceTests.CreateService (context)
                                                                               .ExportAsync (TestContext.Current.CancellationToken),
                                       options: DashboardBackup.JsonOptions);
    }

    Assert.True (condition: DashboardBackupService.TryRead (kinds: DashboardBackupServiceTests.Kinds,
                                                            integrations: DashboardBackupServiceTests.Integrations,
                                                            json: json,
                                                            backup: out DashboardBackup? backup,
                                                            errors: out List<string> errors),
                 userMessage: string.Join (separator: '\n', values: errors));

    await using (DashboardContext context = target.CreateContext ())
    {
      await DashboardBackupServiceTests.CreateService (context)
                                       .ImportAsync (backup: backup!, cancellationToken: TestContext.Current.CancellationToken);
    }

    await using DashboardContext sourceContext = source.CreateContext ();
    await using DashboardContext targetContext = target.CreateContext ();

    Assert.Equal (expected: await DashboardBackupServiceTests.DescribeAsync (sourceContext),
                  actual: await DashboardBackupServiceTests.DescribeAsync (targetContext));
    Assert.Equal (expected: "Development",
                  actual: (await targetContext.DashboardSettings.SingleAsync (TestContext.Current.CancellationToken)).Title);
  }

  [Fact]
  public async Task ImportAsync_ReplacesWhatWasThere ()
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();

    context.Connections.Add (TestConnections.UptimeKuma ());
    await context.SaveChangesAsync (TestContext.Current.CancellationToken);

    var backup = new DashboardBackup (Format: DashboardBackup.FormatName,
                                      Version: DashboardBackup.CurrentVersion,
                                      ExportedAt: null,
                                      Settings: new BackupSettings (Title: "Imported",
                                                                    Motto: null,
                                                                    LocalDomains: null,
                                                                    ColorMode: ColorMode.Dark),
                                      Categories:
                                      [
                                        new BackupCategory (Name: "Only",
                                                            IsAdminCategory: false,
                                                            Links:
                                                            [
                                                              new BackupLink (Label: "One",
                                                                              Url: "https://one.home.arpa/",
                                                                              Description: null,
                                                                              IconName: "One App",
                                                                              IconUrl: null,
                                                                              MonitorId: 5),
                                                            ]),
                                      ],
                                      InfrastructureGroups: [],
                                      Connections: []);

    await DashboardBackupServiceTests.CreateService (context)
                                     .ImportAsync (backup: backup, cancellationToken: TestContext.Current.CancellationToken);

    Link link = await context.Links.Include (l => l.Category).SingleAsync (TestContext.Current.CancellationToken);
    DashboardSettings settings =
      await context.DashboardSettings.AsNoTracking ().SingleAsync (TestContext.Current.CancellationToken);

    Assert.Equal (expected: "Only",    actual: link.Category!.Name);
    Assert.Equal (expected: "one-app", actual: link.IconName);
    Assert.Equal (expected: "",        actual: link.Description);
    Assert.Equal (expected: 5,         actual: link.MonitorId);
    Assert.Empty (await context.InfrastructureGroups.ToListAsync (TestContext.Current.CancellationToken));
    Assert.Equal (expected: "Imported",     actual: settings.Title);
    Assert.Equal (expected: ColorMode.Dark, actual: settings.ColorMode);
    Assert.Empty (await context.Connections.ToListAsync (TestContext.Current.CancellationToken));
  }

  [Theory]
  [InlineData ("not json",                                         "The file isn't valid JSON for a dashboard")]
  [InlineData ("""{ "format": "something-else", "version": 1 }""", "The file isn't an h2s dashboard export.")]
  [InlineData ("""{ "format": "h2s-dashboard", "version": 6 }""",
               "The file is from a newer version of h2s (format version 6); update h2s to import it.")]
  [InlineData ("""{ "format": "h2s-dashboard", "version": 0 }""", "The file's format version (0) isn't supported.")]
  [InlineData ("""{ "format": "h2s-dashboard", "version": 1, "categories": [] }""", "The file has no settings.")]
  public void TryRead_RejectsFilesThatArentDashboards (string json, string error)
  {
    Assert.False (DashboardBackupService.TryRead (kinds: DashboardBackupServiceTests.Kinds,
                                                  integrations: DashboardBackupServiceTests.Integrations,
                                                  json: json,
                                                  backup: out DashboardBackup? backup,
                                                  errors: out List<string> errors));
    Assert.Null (backup);
    Assert.StartsWith (expectedStartString: error, actualString: errors[0]);
  }

  [Fact]
  public void TryRead_ListsEachProblemWithItsPlace ()
  {
    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 1,
                          "settings": { "title": "", "colorMode": "Auto", "uptimeKumaServerUrl": "status", "uptimeKumaDefaultDuration": 0 },
                          "categories": [
                            { "name": "Media", "links": [
                              { "label": "", "url": "ftp://x" },
                              { "label": "Ok", "url": "https://ok.home.arpa/", "iconUrl": "icon.png", "monitorId": -1 }
                            ] },
                            { "name": " ", "links": [] }
                          ],
                          "infrastructureGroups": [ { "name": "", "monitorId": 0 } ]
                        }
                        """;

    Assert.False (DashboardBackupService.TryRead (kinds: DashboardBackupServiceTests.Kinds,
                                                  integrations: DashboardBackupServiceTests.Integrations,
                                                  json: Json,
                                                  backup: out _,
                                                  errors: out List<string> errors));
    Assert.Equal (expected:
                  [
                    "Settings: the title is required.",
                    "Category 1 (\"Media\"), link 1: the label is required.",
                    "Category 1 (\"Media\"), link 1: the URL must be an HTTP or HTTPS URL.",
                    "Category 1 (\"Media\"), link 2: the icon URL must be an HTTP or HTTPS URL.",
                    "Category 1 (\"Media\"), link 2: the monitor ID must be a positive integer.",
                    "Category 2: the name is required.",
                    "Infrastructure group 1: the name is required.",
                    "Infrastructure group 1: the monitor ID must be a positive integer.",
                    "Connection 1 (\"Uptime Kuma\"): Server URL must be an HTTP or HTTPS URL.",
                  ],
                  actual: errors);
  }

  [Fact]
  public async Task ExportThenImport_ReproducesTheConnections_AndKeepsTheSecretsOfThoseTheyReplace ()
  {
    await using var source = await TestDatabase.CreateAsync (seed: true);
    await using var target = await TestDatabase.CreateAsync ();
    string          json;

    await using (DashboardContext context = source.CreateContext ())
    {
      Connection kuma = TestConnections.UptimeKuma (statusPageSlug: "main", defaultDuration: 48);
      kuma.SecretsJson = "never exported";
      context.Connections.Add (kuma);
      await context.SaveChangesAsync (TestContext.Current.CancellationToken);

      json = JsonSerializer.Serialize (value: await DashboardBackupServiceTests.CreateService (context)
                                                                               .ExportAsync (TestContext.Current.CancellationToken),
                                       options: DashboardBackup.JsonOptions);
    }

    Assert.DoesNotContain (expectedSubstring: "never exported", actualString: json);
    Assert.DoesNotContain (expectedSubstring: "uptimeKuma",     actualString: json);

    await using DashboardContext targetContext = target.CreateContext ();

    Connection replaced = TestConnections.UptimeKuma (serverUrl: "https://old.home.arpa");
    replaced.SecretsJson = "kept";
    targetContext.Connections.Add (replaced);
    await targetContext.SaveChangesAsync (TestContext.Current.CancellationToken);
    targetContext.ChangeTracker.Clear ();

    Assert.True (DashboardBackupService.TryRead (json: json,
                                                 kinds: DashboardBackupServiceTests.Kinds,
                                                 integrations: DashboardBackupServiceTests.Integrations,
                                                 backup: out DashboardBackup? backup,
                                                 errors: out _));
    await DashboardBackupServiceTests.CreateService (targetContext)
                                     .ImportAsync (backup: backup!, cancellationToken: TestContext.Current.CancellationToken);

    Connection imported = await targetContext.Connections.AsNoTracking ().SingleAsync (TestContext.Current.CancellationToken);

    Assert.Equal (expected: new UptimeKumaSettings (ServerUrl: TestConnections.KumaServerUrl,
                                                    StatusPageSlug: "main",
                                                    DefaultDuration: 48),
                  actual: await TestConnections.ReadUptimeKumaAsync (targetContext));
    Assert.Equal (expected: "Uptime Kuma", actual: imported.Name);
    Assert.Equal (expected: "kept",        actual: imported.SecretsJson);
  }

  [Theory]
  [InlineData (""" "uptimeKumaServerUrl": " https://status.home.arpa ", "uptimeKumaStatusPageSlug": "main", "uptimeKumaDefaultDuration": 12 """,
               "main",
               12)]
  [InlineData (""" "uptimeKumaServerUrl": "https://status.home.arpa", "uptimeKumaDefaultDuration": 24 """, null, 24)]
  public async Task ImportAsync_MovesOldUptimeKumaSettingsIntoAConnection (string kuma, string? slug, int duration)
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();

    var json = $$"""
                 {
                   "format": "h2s-dashboard", "version": 4,
                   "settings": { "title": "Old", "colorMode": "Auto", {{kuma}} },
                   "categories": [], "panels": [], "searchEngines": []
                 }
                 """;

    Assert.True (condition: DashboardBackupService.TryRead (json: json,
                                                            kinds: DashboardBackupServiceTests.Kinds,
                                                            integrations: DashboardBackupServiceTests.Integrations,
                                                            backup: out DashboardBackup? backup,
                                                            errors: out List<string> errors),
                 userMessage: string.Join (separator: '\n', values: errors));
    Assert.Equal (expected: 1, actual: DashboardBackupService.Summarize (backup!).Connections);

    await DashboardBackupServiceTests.CreateService (context)
                                     .ImportAsync (backup: backup!, cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: new UptimeKumaSettings (ServerUrl: TestConnections.KumaServerUrl,
                                                    StatusPageSlug: slug,
                                                    DefaultDuration: duration),
                  actual: await TestConnections.ReadUptimeKumaAsync (context));
  }

  [Fact]
  public void TryRead_LeavesUptimeKumaOffForAFileFromBeforeConnectionsWithoutAServer ()
  {
    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 4,
                          "settings": { "title": "Old", "colorMode": "Auto", "uptimeKumaServerUrl": " ", "uptimeKumaDefaultDuration": 24 },
                          "categories": [], "panels": [], "searchEngines": []
                        }
                        """;

    Assert.True (DashboardBackupService.TryRead (json: Json,
                                                 kinds: DashboardBackupServiceTests.Kinds,
                                                 integrations: DashboardBackupServiceTests.Integrations,
                                                 backup: out DashboardBackup? backup,
                                                 errors: out _));
    Assert.Empty (backup!.Connections!);
  }

  [Fact]
  public void TryRead_ChecksConnections ()
  {
    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 5,
                          "settings": { "title": "Home", "colorMode": "Auto" },
                          "categories": [], "panels": [], "searchEngines": [],
                          "connections": [
                            { "integration": "uptime-kuma", "name": "Kuma", "options": { "serverUrl": "https://status.home.arpa/dashboard" } },
                            { "integration": "uptime-kuma", "name": "Second", "options": { "serverUrl": "https://other.home.arpa" } },
                            { "integration": "weather", "name": "Weather" },
                            { "integration": "uptime-kuma", "name": " " },
                            { "name": "Nothing" }
                          ]
                        }
                        """;

    Assert.False (DashboardBackupService.TryRead (json: Json,
                                                  kinds: DashboardBackupServiceTests.Kinds,
                                                  integrations: DashboardBackupServiceTests.Integrations,
                                                  backup: out _,
                                                  errors: out List<string> errors));
    Assert.Equal (expected:
                  [
                    "Connection 1 (\"Kuma\"): " + UptimeKumaIntegration.InvalidServerUrl,
                    "Connection 2 (\"Second\"): the dashboard can only have one Uptime Kuma connection.",
                    "Connection 3 (\"Weather\"): h2s has no “weather” integration.",
                    "Connection 4: the name is required.",
                    "Connection 4: the dashboard can only have one Uptime Kuma connection.",
                    "Connection 4: Server URL is required.",
                    "Connection 5 (\"Nothing\"): the integration is required.",
                  ],
                  actual: errors);
  }

  [Fact]
  public void TryRead_RequiresConnectionsFromVersion5 ()
  {
    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 5,
                          "settings": { "title": "Home", "colorMode": "Auto" },
                          "categories": [], "panels": [], "searchEngines": []
                        }
                        """;

    Assert.False (DashboardBackupService.TryRead (json: Json,
                                                  kinds: DashboardBackupServiceTests.Kinds,
                                                  integrations: DashboardBackupServiceTests.Integrations,
                                                  backup: out _,
                                                  errors: out List<string> errors));
    Assert.Equal (expected: ["The file has no connections list."], actual: errors);
  }

  [Fact]
  public void Summarize_CountsWhatTheFileHolds ()
  {
    var backup = new DashboardBackup (Format: DashboardBackup.FormatName,
                                      Version: 1,
                                      ExportedAt: null,
                                      Settings: new BackupSettings (Title: "Home",
                                                                    Motto: null,
                                                                    LocalDomains: null,
                                                                    ColorMode: ColorMode.Auto),
                                      Categories:
                                      [
                                        new BackupCategory (Name: "A",
                                                            IsAdminCategory: false,
                                                            Links:
                                                            [
                                                              new BackupLink (Label: "1",
                                                                              Url: "https://a/",
                                                                              Description: null,
                                                                              IconName: null,
                                                                              IconUrl: null,
                                                                              MonitorId: null),
                                                            ]),
                                        new BackupCategory (Name: "B",
                                                            IsAdminCategory: true,
                                                            Links:
                                                            [
                                                              new BackupLink (Label: "2",
                                                                              Url: "https://b/",
                                                                              Description: null,
                                                                              IconName: null,
                                                                              IconUrl: null,
                                                                              MonitorId: null),
                                                              new BackupLink (Label: "3",
                                                                              Url: "https://c/",
                                                                              Description: null,
                                                                              IconName: null,
                                                                              IconUrl: null,
                                                                              MonitorId: null),
                                                            ]),
                                      ],
                                      InfrastructureGroups: [new BackupInfrastructureGroup (Name: "G", MonitorId: 1)]);

    Assert.Equal (expected: new BackupSummary (Title: "Home",
                                               ExportedAt: null,
                                               Categories: 2,
                                               AdminCategories: 1,
                                               Links: 3,
                                               InfrastructureGroups: 1,
                                               Panels: 2,
                                               SearchEngines: 3,
                                               Connections: 0),
                  actual: DashboardBackupService.Summarize (backup));
  }

  /// <summary>
  ///   Creates the service over a context, with its own settings cache.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <returns>The service.</returns>
  private static DashboardBackupService CreateService (DashboardContext context)
  {
    var cache = new MemoryCache (new MemoryCacheOptions ());

    return new DashboardBackupService (context: context,
                                       settingsService: new DashboardSettingsService (context: context, cache: cache),
                                       connections: TestConnections.CreateService (context: context, cache: cache),
                                       integrations: DashboardBackupServiceTests.Integrations);
  }

  /// <summary>
  ///   Describes a database's search engines, in order.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <returns>Each engine's name, shortcut, default flag and position.</returns>
  private static async Task<List<(string Name, string Shortcut, bool IsDefault, int SortOrder)>> DescribeEnginesAsync (
    DashboardContext context)
    => (await context.SearchEngines.AsNoTracking ()
                     .OrderBy (e => e.SortOrder)
                     .ToListAsync (TestContext.Current.CancellationToken))
      .Select (e => (e.Name, e.Shortcut, e.IsDefault, e.SortOrder))
      .ToList ();

  /// <summary>
  ///   Describes a database's panels, in dashboard order.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <returns>Each panel's kind, zone, position and options.</returns>
  private static async Task<List<(string Kind, PanelZone Zone, int SortOrder, string OptionsJson)>> DescribePanelsAsync (
    DashboardContext context)
    => (await context.Panels.AsNoTracking ()
                     .OrderBy (p => p.Zone)
                     .ThenBy (p => p.SortOrder)
                     .ToListAsync (TestContext.Current.CancellationToken))
      .Select (p => (p.Kind, p.Zone, p.SortOrder, p.OptionsJson))
      .ToList ();

  /// <summary>
  ///   Describes a database's categories and links, in dashboard order, ignoring identifiers.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <returns>One line per category and link.</returns>
  private static async Task<List<string>> DescribeAsync (DashboardContext context)
    => (await context.Categories.Include (c => c.Links)
                     .OrderBy (c => c.IsAdminCategory)
                     .ThenBy (c => c.SortOrder)
                     .ToListAsync (TestContext.Current.CancellationToken))
      .SelectMany (c => new[] { $"{c.Name} admin={c.IsAdminCategory} order={c.SortOrder}" }
                    .Concat (c.Links.OrderBy (l => l.SortOrder)
                              .Select (l => $"  {l.SortOrder} {l.Label} {l.Url} {l.Description} {l.IconName} {l.IconUrl} {l.MonitorId}")))
      .ToList ();
}

using h2s.Data;
using h2s.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for migrations that convert existing data.
/// </summary>
public class MigrationTests
{
  [Fact]
  public async Task ConvertMonitorIdsToIntegers_ConvertsValidIdsAndDiscardsInvalidOnes ()
  {
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);
    var migrator = context.GetService<IMigrator> ();

    // Monitor IDs were text before this migration.
    await migrator.MigrateAsync (targetMigration: "20260503191212_AddLinkMonitorId", cancellationToken: cancellationToken);
    await context.Database.ExecuteSqlRawAsync (sql: """
                                                    INSERT INTO "Categories" ("Id", "Name", "IsAdminCategory") VALUES (1, 'Apps', 0);
                                                    INSERT INTO "Links" ("CategoryId", "Label", "Description", "IconName", "Url", "MonitorId") VALUES
                                                      (1, 'Valid',  '', '', 'https://a.home.arpa', '31'),
                                                      (1, 'Padded', '', '', 'https://b.home.arpa', ' 007 '),
                                                      (1, 'None',   '', '', 'https://c.home.arpa', NULL),
                                                      (1, 'Text',   '', '', 'https://d.home.arpa', 'abc'),
                                                      (1, 'Zero',   '', '', 'https://e.home.arpa', '0'),
                                                      (1, 'Huge',   '', '', 'https://f.home.arpa', '99999999999');
                                                    INSERT INTO "InfrastructureGroups" ("Name", "MonitorId", "SortOrder") VALUES
                                                      ('Good', '16', 0),
                                                      ('Bad', 'n/a', 1);
                                                    """,
                                               cancellationToken: cancellationToken);

    // Links' monitor IDs last until they become widgets.
    await migrator.MigrateAsync (targetMigration: "20261016141249_AddWidgets", cancellationToken: cancellationToken);

    Assert.Equal (expected:
                  [
                    ("Huge", "null", null),
                    ("None", "null", null),
                    ("Padded", "integer", 7L),
                    ("Text", "null", null),
                    ("Valid", "integer", 31L),
                    ("Zero", "null", null),
                  ],
                  actual: await MigrationTests.ReadMonitorIdsAsync (connection: connection,
                                                                    table: "Links",
                                                                    nameColumn: "Label",
                                                                    cancellationToken: cancellationToken));
    Assert.Equal (expected: [("Good", "integer", 16L)],
                  actual: await MigrationTests.ReadMonitorIdsAsync (connection: connection,
                                                                    table: "InfrastructureGroups",
                                                                    nameColumn: "Name",
                                                                    cancellationToken: cancellationToken));
  }

  [Fact]
  public async Task AddCategoryAndLinkSortOrder_KeepsThePreviousDisplayOrder ()
  {
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);
    var migrator = context.GetService<IMigrator> ();

    await migrator.MigrateAsync (targetMigration: "20261013163117_ConvertMonitorIdsToIntegers",
                                 cancellationToken: cancellationToken);
    await context.Database.ExecuteSqlRawAsync (sql: """
                                                    INSERT INTO "Categories" ("Id", "Name", "IsAdminCategory") VALUES
                                                      (1, 'Zeta', 0), (2, 'alpha', 0), (3, 'Beta', 0), (4, 'Ops', 1), (5, 'Backups', 1);
                                                    INSERT INTO "Links" ("Id", "CategoryId", "Label", "Description", "IconName", "Url") VALUES
                                                      (1, 1, 'beta',  '', '', 'https://a.home.arpa'),
                                                      (2, 1, 'Gamma', '', '', 'https://b.home.arpa'),
                                                      (3, 1, 'alpha', '', '', 'https://c.home.arpa'),
                                                      (4, 1, 'Alpha', '', '', 'https://d.home.arpa'),
                                                      (5, 2, 'Solo',  '', '', 'https://e.home.arpa');
                                                    """,
                                               cancellationToken: cancellationToken);

    await migrator.MigrateAsync (cancellationToken: cancellationToken);

    // Categories kept EF Core's binary name order within each section; links kept the page's case-insensitive label order.
    Assert.Equal (expected: [("Backups", 0L), ("Beta", 0L), ("Ops", 1L), ("Zeta", 1L), ("alpha", 2L)],
                  actual: await MigrationTests.ReadSortOrdersAsync (connection: connection,
                                                                    table: "Categories",
                                                                    nameColumn: "Name",
                                                                    cancellationToken: cancellationToken));
    Assert.Equal (expected: [("Alpha", 0L), ("Gamma", 3L), ("Solo", 0L), ("alpha", 1L), ("beta", 2L)],
                  actual: await MigrationTests.ReadSortOrdersAsync (connection: connection,
                                                                    table: "Links",
                                                                    nameColumn: "Label",
                                                                    cancellationToken: cancellationToken));
  }

  [Fact]
  public async Task AddPanels_CreatesTodaysPanelsAboveTheCategories ()
  {
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);

    await context.Database.MigrateAsync (cancellationToken);

    Assert.Equal (expected: [("search", 1L), ("status-bar", 0L)],
                  actual: await MigrationTests.ReadSortOrdersAsync (connection: connection,
                                                                    table: "Panels",
                                                                    nameColumn: "Kind",
                                                                    cancellationToken: cancellationToken));
    Assert.All (collection: await context.Panels.ToListAsync (cancellationToken),
                action: p =>
                        {
                          Assert.Equal (expected: PanelZone.Top, actual: p.Zone);
                          Assert.Equal (expected: "{}",          actual: p.OptionsJson);
                        });
  }

  [Fact]
  public async Task AddSearchEngines_KeepsBingAsTheDefault ()
  {
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);

    await context.Database.MigrateAsync (cancellationToken);

    List<SearchEngine> engines = await context.SearchEngines.OrderBy (e => e.SortOrder).ToListAsync (cancellationToken);

    Assert.Equal (expected: [("Bing", "b", true), ("DuckDuckGo", "d", false), ("Google", "g", false)],
                  actual: engines.Select (e => (e.Name, e.Shortcut, e.IsDefault)));
    Assert.Equal (expected: "https://www.bing.com/search?q={query}", actual: engines[0].UrlTemplate);
  }

  [Theory]
  [InlineData (" https://status.home.arpa ",
               "main",
               48,
               """{"serverUrl":"https://status.home.arpa","statusPageSlug":"main","defaultDuration":48}""",
               "https://status.home.arpa",
               "main",
               48)]
  [InlineData ("https://status.home.arpa",
               null,
               0,
               """{"serverUrl":"https://status.home.arpa","statusPageSlug":"","defaultDuration":24}""",
               "https://status.home.arpa",
               null,
               24)]
  [InlineData (" ",  "main", 12, null, null, null, 24)]
  [InlineData (null, null,   24, null, null, null, 24)]
  public async Task MoveUptimeKumaToConnection_MovesTheSettingsIntoAConnectionAndBack (string? serverUrl,
                                                                                       string? slug,
                                                                                       int     duration,
                                                                                       string? expectedOptions,
                                                                                       string? revertedServerUrl,
                                                                                       string? revertedSlug,
                                                                                       int     revertedDuration)
  {
    const string Before = "20261015222133_AddLinkOpenInNewTab";

    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);
    var migrator = context.GetService<IMigrator> ();

    // The AddDashboardSettings migration inserted the settings row.
    await migrator.MigrateAsync (targetMigration: Before, cancellationToken: cancellationToken);
    await context.Database.ExecuteSqlAsync (sql: $"""
                                                  UPDATE "DashboardSettings"
                                                  SET "UptimeKumaServerUrl" = {serverUrl}, "UptimeKumaStatusPageSlug" = {slug},
                                                      "UptimeKumaDefaultDuration" = {duration}
                                                  """,
                                            cancellationToken: cancellationToken);

    await migrator.MigrateAsync (targetMigration: "20261016030011_MoveUptimeKumaToConnection",
                                 cancellationToken: cancellationToken);

    List<Connection> connections = await context.Connections.AsNoTracking ().ToListAsync (cancellationToken);

    if (expectedOptions == null)
    {
      Assert.Empty (connections);
    }
    else
    {
      Connection kuma = Assert.Single (connections);

      Assert.Equal (expected: ("uptime-kuma", "Uptime Kuma", expectedOptions, (string?)null),
                    actual: (kuma.IntegrationKey, kuma.Name, kuma.OptionsJson, kuma.SecretsJson));
    }

    await migrator.MigrateAsync (targetMigration: Before, cancellationToken: cancellationToken);

    await using SqliteCommand command = connection.CreateCommand ();
    command.CommandText =
      """SELECT "UptimeKumaServerUrl", "UptimeKumaStatusPageSlug", "UptimeKumaDefaultDuration" FROM "DashboardSettings" """;
    await using SqliteDataReader reader = await command.ExecuteReaderAsync (cancellationToken);

    Assert.True (await reader.ReadAsync (cancellationToken));
    Assert.Equal (expected: (revertedServerUrl, revertedSlug, revertedDuration),
                  actual: (reader.IsDBNull (0) ? null : reader.GetString (0), reader.IsDBNull (1) ? null : reader.GetString (1),
                           reader.GetInt64 (2)));
  }

  [Theory]
  [InlineData (24,   "1d")]
  [InlineData (48,   "30d")]
  [InlineData (720,  "30d")]
  [InlineData (8760, "365d")]
  public async Task UseUptimeWindows_TurnsHoursIntoTheShortestWindowThatCoversThem (int hours, string window)
  {
    const string Before = "20261016030011_MoveUptimeKumaToConnection";

    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);
    var migrator = context.GetService<IMigrator> ();

    // The AddPanels migration inserted the status bar, with no options of its own.
    await migrator.MigrateAsync (targetMigration: Before, cancellationToken: cancellationToken);
    var kuma      = $$"""{"serverUrl":"https://status.home.arpa","statusPageSlug":"","defaultDuration":{{hours}}}""";
    var statusBar = $$"""{"showUptime":true,"uptimeHours":{{hours}}}""";
    await context.Database.ExecuteSqlAsync (sql: $"""
                                                  INSERT INTO "Connections" ("IntegrationKey", "Name", "OptionsJson")
                                                  VALUES ('uptime-kuma', 'Uptime Kuma', {kuma});
                                                  UPDATE "Panels" SET "OptionsJson" = {statusBar} WHERE "Kind" = 'status-bar';
                                                  """,
                                            cancellationToken: cancellationToken);

    await migrator.MigrateAsync (targetMigration: "20261016051106_UseUptimeWindows", cancellationToken: cancellationToken);

    Assert.Equal (expected: $$"""{"serverUrl":"https://status.home.arpa","statusPageSlug":"","defaultUptimeWindow":"{{window}}"}""",
                  actual: (await context.Connections.AsNoTracking ().SingleAsync (cancellationToken)).OptionsJson);
    Assert.Equal (expected: $$"""{"showUptime":true,"uptimeWindow":"{{window}}"}""",
                  actual: (await context.Panels.AsNoTracking ()
                                        .SingleAsync (predicate: p => p.Kind == "status-bar",
                                                      cancellationToken: cancellationToken)).OptionsJson);

    // Down turns each window back into its length in hours.
    await migrator.MigrateAsync (targetMigration: Before, cancellationToken: cancellationToken);

    await using SqliteCommand command = connection.CreateCommand ();
    command.CommandText = """SELECT json_extract("OptionsJson", '$.defaultDuration') FROM "Connections" """;

    Assert.Equal (expected: window switch
                            {
                              "1d"  => 24L,
                              "30d" => 720L,
                              _     => 8760L,
                            },
                  actual: (long)(await command.ExecuteScalarAsync (cancellationToken))!);
  }

  [Fact]
  public async Task UseUptimeWindows_LeavesAStatusBarWithoutAPeriodOnTheDefault ()
  {
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);
    var migrator = context.GetService<IMigrator> ();

    await migrator.MigrateAsync (targetMigration: "20261016030011_MoveUptimeKumaToConnection",
                                 cancellationToken: cancellationToken);
    const string NoPeriod = """{"uptimeHours":null}""";
    await context.Database.ExecuteSqlAsync (sql: $"""UPDATE "Panels" SET "OptionsJson" = {NoPeriod} WHERE "Kind" = 'status-bar';""",
                                            cancellationToken: cancellationToken);

    await migrator.MigrateAsync (cancellationToken: cancellationToken);

    Assert.Equal (expected: "{}",
                  actual: (await context.Panels.AsNoTracking ()
                                        .SingleAsync (predicate: p => p.Kind == "status-bar",
                                                      cancellationToken: cancellationToken)).OptionsJson);
  }

  [Fact]
  public async Task ConvertLinkMonitorsToWidgets_MakesEachMonitorAWidget_UnlessTheLinkHasOneForIt ()
  {
    const string Before = "20261016141249_AddWidgets";

    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);
    var migrator = context.GetService<IMigrator> ();

    await migrator.MigrateAsync (targetMigration: Before, cancellationToken: cancellationToken);

    // A plain command, since the widgets' JSON braces would read as format placeholders in ExecuteSqlRawAsync.
    await using (SqliteCommand seed = connection.CreateCommand ())
    {
      seed.CommandText = """
                         INSERT INTO "Connections" ("Id", "IntegrationKey", "Name", "OptionsJson") VALUES
                           (1, 'other', 'Other', '{}'),
                           (2, 'uptime-kuma', 'Uptime Kuma', '{"serverUrl":"https://status.home.arpa"}');
                         INSERT INTO "Categories" ("Id", "Name", "IsAdminCategory", "SortOrder") VALUES (1, 'Apps', 0, 0);
                         INSERT INTO "Links" ("Id", "CategoryId", "Label", "Description", "IconName", "IconUrl", "Url", "SortOrder", "MonitorId") VALUES
                           (1, 1, 'Plain',     '', '', '', 'https://a.home.arpa', 0, 11),
                           (2, 1, 'Widgeted',  '', '', '', 'https://b.home.arpa', 1, 12),
                           (3, 1, 'Number',    '', '', '', 'https://c.home.arpa', 2, 13),
                           (4, 1, 'Text',      '', '', '', 'https://d.home.arpa', 3, 14),
                           (5, 1, 'Elsewhere', '', '', '', 'https://e.home.arpa', 4, 15),
                           (6, 1, 'Full',      '', '', '', 'https://f.home.arpa', 5, 16),
                           (7, 1, 'None',      '', '', '', 'https://g.home.arpa', 6, NULL);
                         INSERT INTO "Widgets" ("LinkId", "ConnectionId", "Kind", "OptionsJson", "RefreshSeconds", "SortOrder") VALUES
                           (2, 2, 'monitor', '{"monitorId":99}', 30, 0),
                           (2, 2, 'monitor', '{"monitorId":98}', NULL, 1),
                           (3, 2, 'monitor', '{"monitorId":13}', NULL, 0),
                           (4, 2, 'monitor', '{"monitorId":"14"}', NULL, 0),
                           (5, 1, 'monitor', '{"monitorId":15}', NULL, 0),
                           (6, 2, 'monitor', '{"monitorId":1}', NULL, 0), (6, 2, 'monitor', '{"monitorId":2}', NULL, 1),
                           (6, 2, 'monitor', '{"monitorId":3}', NULL, 2), (6, 2, 'monitor', '{"monitorId":4}', NULL, 3),
                           (6, 2, 'monitor', '{"monitorId":5}', NULL, 4), (6, 2, 'monitor', '{"monitorId":6}', NULL, 5),
                           (6, 2, 'monitor', '{"monitorId":7}', NULL, 6), (6, 2, 'monitor', '{"monitorId":8}', NULL, 7),
                           (6, 2, 'monitor', '{"monitorId":9}', NULL, 8), (6, 2, 'monitor', '{"monitorId":10}', NULL, 9);
                         """;
      _ = await seed.ExecuteNonQueryAsync (cancellationToken);
    }

    await migrator.MigrateAsync (cancellationToken: cancellationToken);

    List<Widget> widgets = await context.Widgets
                                        .AsNoTracking ()
                                        .Where (w => w.LinkId != 6)
                                        .OrderBy (w => w.LinkId)
                                        .ThenBy (w => w.SortOrder)
                                        .ToListAsync (cancellationToken);

    static string Converted (int monitorId)
    {
      return $$"""{"monitorId":{{monitorId}},"showResponseTime":true,"showUptime":true,"uptimeWindow":""}""";
    }

    Assert.Equal (expected:
                  [
                    // A plain link gets its widget; one with others gets it first, ahead of the rest.
                    (1, 2, "monitor", Converted (11), null, 0),
                    (2, 2, "monitor", Converted (12), null, 0),
                    (2, 2, "monitor", """{"monitorId":99}""", 30, 1),
                    (2, 2, "monitor", """{"monitorId":98}""", null, 2),

                    // One already there for the monitor, as a number or as text, isn't doubled.
                    (3, 2, "monitor", """{"monitorId":13}""", null, 0),
                    (4, 2, "monitor", """{"monitorId":"14"}""", null, 0),

                    // A widget of another integration doesn't count.
                    (5, 2, "monitor", Converted (15), null, 0),
                    (5, 1, "monitor", """{"monitorId":15}""", null, 1),
                  ],
                  actual: widgets.Select (w => (w.LinkId, w.ConnectionId, w.Kind, w.OptionsJson, w.RefreshSeconds, w.SortOrder)));

    // A link with ten widgets already gets no more.
    Assert.Equal (expected: 10,
                  actual: await context.Widgets.CountAsync (predicate: w => w.LinkId == 6, cancellationToken: cancellationToken));

    // Going back restores each link's first monitor, and keeps the widgets.
    await migrator.MigrateAsync (targetMigration: Before, cancellationToken: cancellationToken);

    Assert.Equal (expected:
                  [
                    ("Elsewhere", "integer", 15L),
                    ("Full", "integer", 1L),
                    ("None", "null", null),
                    ("Number", "integer", 13L),
                    ("Plain", "integer", 11L),
                    ("Text", "integer", 14L),
                    ("Widgeted", "integer", 12L),
                  ],
                  actual: await MigrationTests.ReadMonitorIdsAsync (connection: connection,
                                                                    table: "Links",
                                                                    nameColumn: "Label",
                                                                    cancellationToken: cancellationToken));
  }

  [Fact]
  public async Task ConvertLinkMonitorsToWidgets_DropsMonitorsWithoutAKumaConnection ()
  {
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);
    var migrator = context.GetService<IMigrator> ();

    await migrator.MigrateAsync (targetMigration: "20261016141249_AddWidgets", cancellationToken: cancellationToken);
    await context.Database.ExecuteSqlRawAsync (sql: """
                                                    INSERT INTO "Categories" ("Id", "Name", "IsAdminCategory", "SortOrder") VALUES (1, 'Apps', 0, 0);
                                                    INSERT INTO "Links" ("CategoryId", "Label", "Description", "IconName", "IconUrl", "Url", "SortOrder", "MonitorId") VALUES
                                                      (1, 'Plain', '', '', '', 'https://a.home.arpa', 0, 11);
                                                    """,
                                               cancellationToken: cancellationToken);

    await migrator.MigrateAsync (cancellationToken: cancellationToken);

    Assert.Equal (expected: "Plain", actual: (await context.Links.AsNoTracking ().SingleAsync (cancellationToken)).Label);
    Assert.False (await context.Widgets.AnyAsync (cancellationToken));
  }

  [Theory]
  [InlineData (true)]
  [InlineData (false)]
  public async Task ConvertGroupsToStatusBarWidgets_MakesEachGroupAnItemOfTheFirstBar_AndBack (bool showUptime)
  {
    const string Before = "20261017134029_ConvertLinkMonitorsToWidgets";

    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);
    var migrator = context.GetService<IMigrator> ();

    // The AddPanels migration inserted the status bar.
    await migrator.MigrateAsync (targetMigration: Before, cancellationToken: cancellationToken);

    await using (SqliteCommand seed = connection.CreateCommand ())
    {
      seed.CommandText = $$"""
                           INSERT INTO "Connections" ("Id", "IntegrationKey", "Name", "OptionsJson") VALUES
                             (1, 'uptime-kuma', 'Uptime Kuma', '{"serverUrl":"https://status.home.arpa/","statusPageSlug":"main","defaultUptimeWindow":"1d"}');
                           UPDATE "Panels" SET "OptionsJson" = '{"showUptime":{{(showUptime ? "true" : "false")}},"uptimeWindow":"30d"}'
                           WHERE "Kind" = 'status-bar';
                           INSERT INTO "InfrastructureGroups" ("Name", "MonitorId", "SortOrder") VALUES
                             ('Storage', 2, 1), (' Compute ', 1, 0), ('Broken', 0, 2);
                           """;
      _ = await seed.ExecuteNonQueryAsync (cancellationToken);
    }

    await migrator.MigrateAsync (cancellationToken: cancellationToken);

    Panel bar = await context.Panels
                             .AsNoTracking ()
                             .Include (p => p.Widgets)
                             .SingleAsync (predicate: p => p.Kind == "status-bar", cancellationToken: cancellationToken);
    string show = showUptime ? "true" : "false";

    Assert.Equal (expected: """{"linkUrl":"https://status.home.arpa/status/main"}""", actual: bar.OptionsJson);
    Assert.Equal (expected:
                  [
                    ("Compute", $$"""{"monitorId":1,"showResponseTime":false,"showUptime":{{show}},"uptimeWindow":"30d"}""", 0),
                    ("Storage", $$"""{"monitorId":2,"showResponseTime":false,"showUptime":{{show}},"uptimeWindow":"30d"}""", 1),
                  ],
                  actual: bar.Widgets.OrderBy (w => w.SortOrder).Select (w => (w.Label, w.OptionsJson, w.SortOrder)));
    Assert.All (collection: bar.Widgets,
                action: w => Assert.Equal (expected: (1, "monitor", (int?)null), actual: (w.ConnectionId, w.Kind, w.LinkId)));
    Assert.Equal (expected: """{"serverUrl":"https://status.home.arpa/","defaultUptimeWindow":"1d"}""",
                  actual: (await context.Connections.AsNoTracking ().SingleAsync (cancellationToken)).OptionsJson);

    // Going back makes the first bar's Kuma items groups again, and its link the status page.
    await migrator.MigrateAsync (targetMigration: Before, cancellationToken: cancellationToken);

    Assert.Equal (expected: [("Compute", 0L), ("Storage", 1L)],
                  actual: await MigrationTests.ReadSortOrdersAsync (connection: connection,
                                                                    table: "InfrastructureGroups",
                                                                    nameColumn: "Name",
                                                                    cancellationToken: cancellationToken));

    await using SqliteCommand slug = connection.CreateCommand ();
    slug.CommandText = """SELECT json_extract("OptionsJson", '$.statusPageSlug') FROM "Connections" """;

    Assert.Equal (expected: "main", actual: await slug.ExecuteScalarAsync (cancellationToken));
  }

  [Fact]
  public async Task ConvertGroupsToStatusBarWidgets_DropsTheGroupsWithoutAKumaConnection ()
  {
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    await using var   connection        = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (cancellationToken);

    await using var context =
      new DashboardContext (new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options);
    var migrator = context.GetService<IMigrator> ();

    await migrator.MigrateAsync (targetMigration: "20261017134029_ConvertLinkMonitorsToWidgets",
                                 cancellationToken: cancellationToken);
    await context.Database.ExecuteSqlRawAsync (sql:
                                               """INSERT INTO "InfrastructureGroups" ("Name", "MonitorId", "SortOrder") VALUES ('Compute', 1, 0);""",
                                               cancellationToken: cancellationToken);

    await migrator.MigrateAsync (cancellationToken: cancellationToken);

    Assert.False (await context.Widgets.AnyAsync (cancellationToken));
    Assert.Equal (expected: "{}",
                  actual: (await context.Panels.AsNoTracking ()
                                        .SingleAsync (predicate: p => p.Kind == "status-bar", cancellationToken: cancellationToken))
                 .OptionsJson);
  }

  /// <summary>
  ///   Reads each row's name with its sort order.
  /// </summary>
  /// <param name="connection">The open connection to the database.</param>
  /// <param name="table">The table to read.</param>
  /// <param name="nameColumn">The column that names each row.</param>
  /// <param name="cancellationToken">A token that cancels the read.</param>
  /// <returns>The rows, ordered by name.</returns>
  private static async Task<List<(string Name, long SortOrder)>> ReadSortOrdersAsync (SqliteConnection  connection,
                                                                                      string            table,
                                                                                      string            nameColumn,
                                                                                      CancellationToken cancellationToken)
  {
    await using SqliteCommand command = connection.CreateCommand ();
    command.CommandText = $"""SELECT "{nameColumn}", "SortOrder" FROM "{table}" ORDER BY "{nameColumn}" """;

    var                          rows   = new List<(string Name, long SortOrder)> ();
    await using SqliteDataReader reader = await command.ExecuteReaderAsync (cancellationToken);

    while (await reader.ReadAsync (cancellationToken))
    {
      rows.Add ((reader.GetString (0), reader.GetInt64 (1)));
    }

    return rows;
  }

  /// <summary>
  ///   Reads each row's name with the storage class and value of its monitor ID.
  /// </summary>
  /// <param name="connection">The open connection to the database.</param>
  /// <param name="table">The table to read.</param>
  /// <param name="nameColumn">The column that names each row.</param>
  /// <param name="cancellationToken">A token that cancels the read.</param>
  /// <returns>The rows, ordered by name.</returns>
  private static async Task<List<(string Name, string Type, long? MonitorId)>> ReadMonitorIdsAsync (SqliteConnection connection,
                                                                                                    string           table,
                                                                                                    string           nameColumn,
                                                                                                    CancellationToken
                                                                                                      cancellationToken)
  {
    await using SqliteCommand command = connection.CreateCommand ();
    command.CommandText = $"""SELECT "{nameColumn}", typeof("MonitorId"), "MonitorId" FROM "{table}" ORDER BY "{nameColumn}" """;

    var                          rows   = new List<(string Name, string Type, long? MonitorId)> ();
    await using SqliteDataReader reader = await command.ExecuteReaderAsync (cancellationToken);

    while (await reader.ReadAsync (cancellationToken))
    {
      rows.Add ((reader.GetString (0), reader.GetString (1), reader.IsDBNull (2) ? null : reader.GetInt64 (2)));
    }

    return rows;
  }
}

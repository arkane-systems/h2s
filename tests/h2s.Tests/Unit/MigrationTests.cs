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

    await migrator.MigrateAsync (cancellationToken: cancellationToken);

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
      Assert.Empty (connections);
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

    await migrator.MigrateAsync (cancellationToken: cancellationToken);

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
      rows.Add ((reader.GetString (0), reader.GetInt64 (1)));

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
      rows.Add ((reader.GetString (0), reader.GetString (1), reader.IsDBNull (2) ? null : reader.GetInt64 (2)));

    return rows;
  }
}

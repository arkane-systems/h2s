using h2s.Data;
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

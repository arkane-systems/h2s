using h2s.Data;
using h2s.Data.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Infrastructure;

/// <summary>
///   A migrated, in-memory SQLite dashboard database for unit tests. The database lives as long as the instance.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
  private readonly SqliteConnection                   connection;
  private readonly DbContextOptions<DashboardContext> options;

  /// <summary>
  ///   Initializes a new instance of the <see cref="TestDatabase" /> class.
  /// </summary>
  /// <param name="connection">The open connection that keeps the in-memory database alive.</param>
  private TestDatabase (SqliteConnection connection)
  {
    this.connection = connection;
    this.options    = new DbContextOptionsBuilder<DashboardContext> ().UseSqlite (connection).Options;
  }

  /// <summary>
  ///   Creates a migrated database, optionally filled with <see cref="DevDataSeeder" /> sample data.
  /// </summary>
  /// <param name="seed">Whether to seed the sample data.</param>
  /// <returns>The test database.</returns>
  public static async Task<TestDatabase> CreateAsync (bool seed = false)
  {
    var connection = new SqliteConnection ("Data Source=:memory:");
    await connection.OpenAsync (TestContext.Current.CancellationToken);

    var database = new TestDatabase (connection);

    await using DashboardContext context = database.CreateContext ();
    await context.Database.MigrateAsync (TestContext.Current.CancellationToken);

    if (seed)
    {
      await DevDataSeeder.SeedAsync (context: context, cancellationToken: TestContext.Current.CancellationToken);
    }

    return database;
  }

  /// <summary>
  ///   Creates a new context over the test database. Callers own and dispose it.
  /// </summary>
  /// <returns>The database context.</returns>
  public DashboardContext CreateContext () => new (this.options);

  /// <inheritdoc />
  public ValueTask DisposeAsync () => this.connection.DisposeAsync ();
}

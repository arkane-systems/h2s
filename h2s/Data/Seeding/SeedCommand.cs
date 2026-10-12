using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace h2s.Data.Seeding;

/// <summary>
/// Implements the development-only <c>seed</c> command-line verb, which builds a fresh development database.
/// </summary>
/// <remarks>
/// Usage: <c>dotnet run --project h2s/h2s.csproj -- seed [--reset] [--from &lt;path&gt;]</c>.
/// Without <c>--from</c>, the database is migrated and filled with <see cref="DevDataSeeder"/> sample data. With
/// <c>--from</c>, an existing database (for example a copy of production) is snapshotted using SQLite's online backup
/// API and then migrated to the current schema. <c>--reset</c> discards any existing development database first.
/// </remarks>
public static class SeedCommand
{
  private const string Verb = "seed";
  private const string ResetOption = "--reset";
  private const string FromOption = "--from";

  /// <summary>
  /// Determines whether the command-line arguments invoke the <c>seed</c> verb.
  /// </summary>
  /// <param name="args">The process command-line arguments.</param>
  /// <returns><c>true</c> when the first argument is the <c>seed</c> verb; otherwise, <c>false</c>.</returns>
  public static bool IsInvoked (string[] args) => args is [SeedCommand.Verb, ..];

  /// <summary>
  /// Runs the <c>seed</c> verb against the application's configured database.
  /// </summary>
  /// <param name="app">The built application, used for its environment and services.</param>
  /// <param name="args">The process command-line arguments, starting with the <c>seed</c> verb.</param>
  /// <returns>The process exit code.</returns>
  public static async Task<int> RunAsync (WebApplication app, string[] args)
  {
    ArgumentNullException.ThrowIfNull (app);
    ArgumentNullException.ThrowIfNull (args);

    if (!app.Environment.IsDevelopment ())
    {
      await Console.Error.WriteLineAsync ("The seed command is only available in the Development environment.");

      return 1;
    }

    if (!SeedCommand.TryParseOptions (args, out var reset, out var fromPath, out var error))
    {
      await Console.Error.WriteLineAsync (error);
      await Console.Error.WriteLineAsync ("Usage: seed [--reset] [--from <path>]");

      return 2;
    }

    using var scope = app.Services.CreateScope ();
    var context = scope.ServiceProvider.GetRequiredService<DashboardContext> ();
    var connectionString = context.Database.GetConnectionString ()
                           ?? throw new InvalidOperationException ("The dashboard database has no connection string.");
    var databasePath = new SqliteConnectionStringBuilder (connectionString).DataSource;

    if (File.Exists (databasePath))
    {
      if (!reset)
      {
        await Console.Error.WriteLineAsync ($"A database already exists at {databasePath}; use {SeedCommand.ResetOption} to replace it.");

        return 1;
      }

      SqliteConnection.ClearAllPools ();
      await context.Database.EnsureDeletedAsync ();
    }

    if (fromPath is not null)
    {
      SqliteConnectionStrings.EnsureDirectoryExists (connectionString);
      SeedCommand.Snapshot (fromPath, connectionString);
      await context.Database.MigrateAsync ();
      await Console.Out.WriteLineAsync ($"Snapshotted {fromPath} to {databasePath} and applied migrations.");

      return 0;
    }

    await context.Database.MigrateAsync ();

    try
    {
      await DevDataSeeder.SeedAsync (context);
    }
    catch (InvalidOperationException ex)
    {
      await Console.Error.WriteLineAsync (ex.Message);

      return 1;
    }

    await Console.Out.WriteLineAsync ($"Created and seeded {databasePath}.");

    return 0;
  }

  /// <summary>
  /// Parses the options that follow the <c>seed</c> verb.
  /// </summary>
  /// <param name="args">The process command-line arguments, starting with the <c>seed</c> verb.</param>
  /// <param name="reset">Receives whether <c>--reset</c> was specified.</param>
  /// <param name="fromPath">Receives the full path given with <c>--from</c>, if any.</param>
  /// <param name="error">Receives a description of the problem when parsing fails.</param>
  /// <returns><c>true</c> when the options are valid; otherwise, <c>false</c>.</returns>
  private static bool TryParseOptions (string[] args, out bool reset, out string? fromPath, out string error)
  {
    reset    = false;
    fromPath = null;
    error    = "";

    for (var index = 1; index < args.Length; index++)
    {
      switch (args[index])
      {
        case SeedCommand.ResetOption:
          reset = true;

          break;

        case SeedCommand.FromOption when index + 1 < args.Length:
          fromPath = Path.GetFullPath (args[++index]);

          if (!File.Exists (fromPath))
          {
            error = $"The source database {fromPath} does not exist.";

            return false;
          }

          break;

        case SeedCommand.FromOption:
          error = $"{SeedCommand.FromOption} requires a path.";

          return false;

        default:
          error = $"Unrecognized option '{args[index]}'.";

          return false;
      }
    }

    return true;
  }

  /// <summary>
  /// Copies a SQLite database using the online backup API, which is safe even while the source is in use.
  /// </summary>
  /// <param name="sourcePath">The path of the database to copy.</param>
  /// <param name="destinationConnectionString">The connection string of the database to create.</param>
  private static void Snapshot (string sourcePath, string destinationConnectionString)
  {
    var sourceConnectionString = new SqliteConnectionStringBuilder
                                 {
                                   DataSource = sourcePath,
                                   Mode       = SqliteOpenMode.ReadOnly,
                                   Pooling    = false,
                                 }.ToString ();

    using var source = new SqliteConnection (sourceConnectionString);
    using var destination = new SqliteConnection (destinationConnectionString);

    source.Open ();
    destination.Open ();
    source.BackupDatabase (destination);
  }
}

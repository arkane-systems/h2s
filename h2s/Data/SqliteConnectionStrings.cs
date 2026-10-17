using Microsoft.Data.Sqlite;

namespace h2s.Data;

/// <summary>
///   Helpers for normalizing SQLite connection strings used by the dashboard database.
/// </summary>
public static class SqliteConnectionStrings
{
  /// <summary>
  ///   Resolves a relative SQLite <c>Data Source</c> against the application content root, so that the database
  ///   location does not depend on the process working directory.
  /// </summary>
  /// <param name="connectionString">The configured SQLite connection string.</param>
  /// <param name="contentRootPath">The application content root path.</param>
  /// <returns>
  ///   The connection string with an absolute <c>Data Source</c>, or the original connection string when the data
  ///   source is already absolute or refers to an in-memory database.
  /// </returns>
  public static string ResolveDataSource (string connectionString, string contentRootPath)
  {
    ArgumentNullException.ThrowIfNull (connectionString);
    ArgumentNullException.ThrowIfNull (contentRootPath);

    var builder = new SqliteConnectionStringBuilder (connectionString);

    if (SqliteConnectionStrings.IsInMemory (builder) || Path.IsPathRooted (builder.DataSource))
    {
      return connectionString;
    }

    builder.DataSource = Path.GetFullPath (Path.Combine (path1: contentRootPath, path2: builder.DataSource));

    return builder.ToString ();
  }

  /// <summary>
  ///   Ensures that the directory containing a file-based SQLite database exists.
  /// </summary>
  /// <param name="connectionString">A SQLite connection string with an absolute <c>Data Source</c>.</param>
  public static void EnsureDirectoryExists (string connectionString)
  {
    string? directory = SqliteConnectionStrings.GetDataDirectory (connectionString);

    if (directory != null)
    {
      Directory.CreateDirectory (directory);
    }
  }

  /// <summary>
  ///   Gets the directory that holds a file-based SQLite database, where other persistent application data lives too.
  /// </summary>
  /// <param name="connectionString">A SQLite connection string with an absolute <c>Data Source</c>.</param>
  /// <returns>The database directory, or <c>null</c> for an in-memory database.</returns>
  public static string? GetDataDirectory (string connectionString)
  {
    ArgumentNullException.ThrowIfNull (connectionString);

    var builder = new SqliteConnectionStringBuilder (connectionString);

    if (SqliteConnectionStrings.IsInMemory (builder))
    {
      return null;
    }

    string? directory = Path.GetDirectoryName (builder.DataSource);

    return string.IsNullOrEmpty (directory) ? null : directory;
  }

  /// <summary>
  ///   Determines whether a connection string refers to an in-memory SQLite database.
  /// </summary>
  /// <param name="builder">The parsed connection string.</param>
  /// <returns><c>true</c> for an in-memory database; otherwise, <c>false</c>.</returns>
  private static bool IsInMemory (SqliteConnectionStringBuilder builder)
    => (builder.Mode == SqliteOpenMode.Memory)   ||
       string.IsNullOrEmpty (builder.DataSource) ||
       string.Equals (a: builder.DataSource, b: ":memory:", comparisonType: StringComparison.OrdinalIgnoreCase);
}

using h2s.Data;
using Microsoft.Data.Sqlite;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="SqliteConnectionStrings" />.
/// </summary>
public class SqliteConnectionStringsTests
{
  private static readonly string ContentRoot = Path.Combine (path1: Path.GetTempPath (), path2: "h2s-content-root");

  [Fact]
  public void EnsureDirectoryExists_CreatesParentDirectory ()
  {
    string directory = Path.Combine (path1: Path.GetTempPath (), path2: $"h2s-test-{Guid.NewGuid ():N}");

    try
    {
      SqliteConnectionStrings.EnsureDirectoryExists ($"Data Source={Path.Combine (path1: directory, path2: "h2s.db")}");

      Assert.True (Directory.Exists (directory));
    }
    finally
    {
      Directory.Delete (path: directory, recursive: true);
    }
  }

  [Fact]
  public void GetDataDirectory_ReturnsDatabaseDirectory ()
  {
    string directory = Path.Combine (path1: SqliteConnectionStringsTests.ContentRoot, path2: "App_Data");

    Assert.Equal (expected: directory,
                  actual:
                  SqliteConnectionStrings.GetDataDirectory ($"Data Source={Path.Combine (path1: directory, path2: "h2s.db")}"));
  }

  [Theory]
  [InlineData ("Data Source=:memory:")]
  [InlineData ("Data Source=shared;Mode=Memory;Cache=Shared")]
  public void GetDataDirectory_ReturnsNullForInMemoryDatabases (string connectionString)
    => Assert.Null (SqliteConnectionStrings.GetDataDirectory (connectionString));

  [Fact]
  public void ResolveDataSource_KeepsAbsolutePath ()
  {
    string absolute         = Path.Combine (path1: Path.GetTempPath (), path2: "elsewhere", path3: "h2s.db");
    var    connectionString = $"Data Source={absolute}";

    Assert.Equal (expected: connectionString,
                  actual: SqliteConnectionStrings.ResolveDataSource (connectionString: connectionString,
                                                                     contentRootPath: SqliteConnectionStringsTests.ContentRoot));
  }

  [Theory]
  [InlineData ("Data Source=:memory:")]
  [InlineData ("Data Source=shared;Mode=Memory;Cache=Shared")]
  public void ResolveDataSource_KeepsInMemoryDatabases (string connectionString)
    => Assert.Equal (expected: connectionString,
                     actual: SqliteConnectionStrings.ResolveDataSource (connectionString: connectionString,
                                                                        contentRootPath: SqliteConnectionStringsTests.ContentRoot));

  [Fact]
  public void ResolveDataSource_ResolvesRelativePathAgainstContentRoot ()
  {
    string resolved = SqliteConnectionStrings.ResolveDataSource (connectionString: "Data Source=App_Data/h2s.db;Cache=Shared",
                                                                 contentRootPath: SqliteConnectionStringsTests.ContentRoot);
    var builder = new SqliteConnectionStringBuilder (resolved);

    Assert.Equal (expected: Path.Combine (path1: SqliteConnectionStringsTests.ContentRoot, path2: "App_Data", path3: "h2s.db"),
                  actual: builder.DataSource);
    Assert.Equal (expected: SqliteCacheMode.Shared, actual: builder.Cache);
  }
}

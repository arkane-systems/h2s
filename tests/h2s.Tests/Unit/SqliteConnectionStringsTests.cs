using h2s.Data;
using Microsoft.Data.Sqlite;

namespace h2s.Tests.Unit;

/// <summary>
/// Tests for <see cref="SqliteConnectionStrings"/>.
/// </summary>
public class SqliteConnectionStringsTests
{
  private static readonly string ContentRoot = Path.Combine (Path.GetTempPath (), "h2s-content-root");

  [Fact]
  public void EnsureDirectoryExists_CreatesParentDirectory ()
  {
    var directory = Path.Combine (Path.GetTempPath (), $"h2s-test-{Guid.NewGuid ():N}");

    try
    {
      SqliteConnectionStrings.EnsureDirectoryExists ($"Data Source={Path.Combine (directory, "h2s.db")}");

      Assert.True (Directory.Exists (directory));
    }
    finally
    {
      Directory.Delete (directory, recursive: true);
    }
  }

  [Fact]
  public void ResolveDataSource_KeepsAbsolutePath ()
  {
    var absolute         = Path.Combine (Path.GetTempPath (), "elsewhere", "h2s.db");
    var connectionString = $"Data Source={absolute}";

    Assert.Equal (connectionString, SqliteConnectionStrings.ResolveDataSource (connectionString, SqliteConnectionStringsTests.ContentRoot));
  }

  [Theory]
  [InlineData ("Data Source=:memory:")]
  [InlineData ("Data Source=shared;Mode=Memory;Cache=Shared")]
  public void ResolveDataSource_KeepsInMemoryDatabases (string connectionString)
    => Assert.Equal (connectionString, SqliteConnectionStrings.ResolveDataSource (connectionString, SqliteConnectionStringsTests.ContentRoot));

  [Fact]
  public void ResolveDataSource_ResolvesRelativePathAgainstContentRoot ()
  {
    var resolved = SqliteConnectionStrings.ResolveDataSource ("Data Source=App_Data/h2s.db;Cache=Shared", SqliteConnectionStringsTests.ContentRoot);
    var builder  = new SqliteConnectionStringBuilder (resolved);

    Assert.Equal (Path.Combine (SqliteConnectionStringsTests.ContentRoot, "App_Data", "h2s.db"), builder.DataSource);
    Assert.Equal (SqliteCacheMode.Shared, builder.Cache);
  }
}

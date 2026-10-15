using h2s.Services;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="AppVersion" />.
/// </summary>
public class AppVersionTests
{
  [Theory]
  [InlineData ("0.4",                                          "0.4")]
  [InlineData ("0.4+019d2f22b6bf96c15814b58c0069db6ac9e4ac15", "0.4")]
  [InlineData ("1.2.3-beta+abc",                               "1.2.3-beta")]
  [InlineData ("",                                             "")]
  [InlineData (null,                                           "")]
  public void FromInformationalVersion_DropsSourceRevision (string? informationalVersion, string expected)
    => Assert.Equal (expected: expected, actual: AppVersion.FromInformationalVersion (informationalVersion));

  [Fact]
  public void Current_IsTheProjectVersion ()
  {
    // h2s.csproj sets <Version>; the SDK may append "+sha", which Current drops.
    Assert.Matches (expectedRegexPattern: @"^\d+\.\d+(\.\d+)?$", actualString: AppVersion.Current);
  }
}

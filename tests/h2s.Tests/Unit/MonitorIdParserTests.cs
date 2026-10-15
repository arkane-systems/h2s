using h2s.Validation;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="MonitorIdParser" />.
/// </summary>
public class MonitorIdParserTests
{
  [Theory]
  [InlineData ("1",          1)]
  [InlineData ("42",         42)]
  [InlineData (" 7 ",        7)]
  [InlineData ("2147483647", int.MaxValue)]
  public void TryParse_AcceptsPositiveIntegers (string value, int expected)
  {
    Assert.True (MonitorIdParser.TryParse (value: value, monitorId: out int monitorId));
    Assert.Equal (expected: expected, actual: monitorId);
  }

  [Theory]
  [InlineData (null)]
  [InlineData ("")]
  [InlineData ("0")]
  [InlineData ("-3")]
  [InlineData ("+3")]
  [InlineData ("1.5")]
  [InlineData ("1,000")]
  [InlineData ("abc")]
  [InlineData ("99999999999")]
  public void TryParse_RejectsOtherValues (string? value)
    => Assert.False (MonitorIdParser.TryParse (value: value, monitorId: out _));

  [Theory]
  [InlineData (null)]
  [InlineData ("")]
  [InlineData ("   ")]
  public void TryParseOptional_TreatsBlankAsNone (string? value)
  {
    Assert.True (MonitorIdParser.TryParseOptional (value: value, monitorId: out int? monitorId));
    Assert.Null (monitorId);
  }

  [Fact]
  public void TryParseOptional_ParsesValue ()
  {
    Assert.True (MonitorIdParser.TryParseOptional (value: "12", monitorId: out int? monitorId));
    Assert.Equal (expected: 12, actual: monitorId);
  }

  [Fact]
  public void TryParseOptional_RejectsInvalidValue ()
    => Assert.False (MonitorIdParser.TryParseOptional (value: "0", monitorId: out _));
}

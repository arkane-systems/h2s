using h2s.Validation;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="OrderedIdParser" />.
/// </summary>
public class OrderedIdParserTests
{
  [Fact]
  public void TryParse_KeepsTheOrder ()
  {
    Assert.True (OrderedIdParser.TryParse (values: ["3", " 1 ", "2"], ids: out List<int>? ids, error: out string? error));
    Assert.Equal (expected: [3, 1, 2], actual: ids);
    Assert.Null (error);
  }

  [Theory]
  [InlineData (new string[0],      "Ordered IDs are required.")]
  [InlineData (new[] { "1", "" },  "Ordered IDs must contain valid positive integers.")]
  [InlineData (new[] { "0" },      "Ordered IDs must contain valid positive integers.")]
  [InlineData (new[] { "-1" },     "Ordered IDs must contain valid positive integers.")]
  [InlineData (new[] { "1e3" },    "Ordered IDs must contain valid positive integers.")]
  [InlineData (new[] { "abc" },    "Ordered IDs must contain valid positive integers.")]
  [InlineData (new[] { "2", "2" }, "Ordered IDs must not repeat an ID.")]
  public void TryParse_RejectsInvalidLists (string[] values, string expectedError)
  {
    Assert.False (OrderedIdParser.TryParse (values: values, ids: out List<int>? ids, error: out string? error));
    Assert.Null (ids);
    Assert.Equal (expected: expectedError, actual: error);
  }

  [Fact]
  public void TryParse_RejectsAMissingList ()
  {
    Assert.False (OrderedIdParser.TryParse (values: null, ids: out _, error: out string? error));
    Assert.Equal (expected: "Ordered IDs are required.", actual: error);
  }
}

using h2s.Validation;

namespace h2s.Tests.Unit;

/// <summary>
/// Tests for the custom validation attributes in <c>h2s.Validation</c>.
/// </summary>
public class ValidationAttributeTests
{
  [Theory]
  [InlineData ("https://status.example.com")]
  [InlineData ("https://status.example.com/")]
  [InlineData ("http://kuma.home.arpa:3001")]
  public void HttpBaseUrl_AcceptsBaseUrls (string value) => Assert.True (new HttpBaseUrlAttribute ().IsValid (value));

  [Theory]
  [InlineData (null)]
  [InlineData ("")]
  [InlineData ("   ")]
  public void HttpBaseUrl_AcceptsBlank (string? value) => Assert.True (new HttpBaseUrlAttribute ().IsValid (value));

  [Theory]
  [InlineData ("ftp://status.example.com")]
  [InlineData ("https://status.example.com/status/main")]
  [InlineData ("https://status.example.com/?page=1")]
  [InlineData ("https://status.example.com/#top")]
  [InlineData ("https://user:secret@status.example.com")]
  [InlineData ("status.example.com")]
  [InlineData ("not a url")]
  public void HttpBaseUrl_RejectsOtherValues (string value) => Assert.False (new HttpBaseUrlAttribute ().IsValid (value));

  [Fact]
  public void HttpBaseUrl_RejectsNonStrings () => Assert.False (new HttpBaseUrlAttribute ().IsValid (42));

  [Theory]
  [InlineData ("1")]
  [InlineData ("42")]
  [InlineData (null)]
  [InlineData ("")]
  public void PositiveIntegerString_AcceptsPositiveIntegersAndBlank (string? value)
    => Assert.True (new PositiveIntegerStringAttribute ().IsValid (value));

  [Theory]
  [InlineData ("0")]
  [InlineData ("-3")]
  [InlineData ("1.5")]
  [InlineData ("abc")]
  [InlineData ("99999999999")]
  public void PositiveIntegerString_RejectsOtherValues (string value)
    => Assert.False (new PositiveIntegerStringAttribute ().IsValid (value));

  [Fact]
  public void PositiveIntegerString_RejectsNonStrings () => Assert.False (new PositiveIntegerStringAttribute ().IsValid (5));
}

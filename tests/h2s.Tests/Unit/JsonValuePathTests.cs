using System.Text.Json;
using h2s.Integrations.JsonApi;
using h2s.Tests.Infrastructure;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="JsonValuePath" />: its syntax and what it finds in a document.
/// </summary>
public class JsonValuePathTests
{
  private static readonly JsonElement Document = JsonDocument.Parse (TestConnections.HttpbinJson).RootElement.Clone ();

  [Theory]
  [InlineData ("slideshow.title",                     "Sample Slide Show")]
  [InlineData ("$.slideshow.title",                   "Sample Slide Show")]
  [InlineData ("  slideshow.title  ",                 "Sample Slide Show")]
  [InlineData ("slideshow.slides[0].title",           "Wake up to WonderWidgets!")]
  [InlineData ("slideshow.slides[1].title",           "Overview")]
  [InlineData ("slideshow.slides[1].items[0]",        "Why <em>WonderWidgets</em> are great")]
  [InlineData ("slideshow[\"author\"]",               "Yours Truly")]
  [InlineData ("slideshow['author']",                 "Yours Truly")]
  [InlineData ("[\"slideshow\"].slides[0][\"type\"]", "all")]
  public void Evaluate_FindsTheValue (string path, string expected)
  {
    JsonElement value = JsonValuePathTests.Find (path);

    Assert.Equal (expected: expected, actual: value.GetString ());
  }

  [Theory]
  [InlineData ("")]
  [InlineData ("   ")]
  [InlineData ("$")]
  public void Evaluate_TheWholeDocument_ForAnEmptyPathOrDollar (string path)
  {
    JsonElement value = JsonValuePathTests.Find (path);

    Assert.Equal (expected: JsonValueKind.Object, actual: value.ValueKind);
    Assert.True (value.TryGetProperty (propertyName: "slideshow", value: out _));
  }

  [Fact]
  public void Evaluate_QuotedKeys_CanHoldDotsSpacesBracketsAndQuotes ()
  {
    using JsonDocument document =
      JsonDocument.Parse ("""{ "a.b": { "odd key": 1, "x[0]": 2, "say \"hi\"": 3, "back\\slash": 4 } }""");

    Assert.Equal (expected: 1,
                  actual: JsonValuePathTests.Find (path: """["a.b"]["odd key"]""", root: document.RootElement).GetInt32 ());
    Assert.Equal (expected: 2,
                  actual: JsonValuePathTests.Find (path: """["a.b"]["x[0]"]""", root: document.RootElement).GetInt32 ());
    Assert.Equal (expected: 3,
                  actual: JsonValuePathTests.Find (path: """["a.b"]["say \"hi\""]""", root: document.RootElement).GetInt32 ());
    Assert.Equal (expected: 4,
                  actual: JsonValuePathTests.Find (path: """["a.b"]["back\\slash"]""", root: document.RootElement).GetInt32 ());
  }

  [Fact]
  public void Evaluate_AKeyThatStartsWithDollar_IsAKey ()
  {
    using JsonDocument document = JsonDocument.Parse ("""{ "$ref": "x" }""");

    Assert.Equal (expected: "x", actual: JsonValuePathTests.Find (path: "$ref", root: document.RootElement).GetString ());
  }

  [Fact]
  public void Evaluate_AnArrayAtTheRoot_TakesAnIndex ()
  {
    using JsonDocument document = JsonDocument.Parse ("""[ "a", { "b": [ 10, 20 ] } ]""");

    Assert.Equal (expected: 20, actual: JsonValuePathTests.Find (path: "[1].b[1]",  root: document.RootElement).GetInt32 ());
    Assert.Equal (expected: 20, actual: JsonValuePathTests.Find (path: "$[1].b[1]", root: document.RootElement).GetInt32 ());
  }

  [Theory]
  [InlineData ("slideshow.nothing")]
  [InlineData ("nothing")]
  [InlineData ("slideshow.slides[2]")]
  [InlineData ("slideshow.slides[99].title")]
  [InlineData ("slideshow.slides.title")]
  [InlineData ("slideshow.title[0]")]
  [InlineData ("slideshow.title.length")]
  [InlineData ("slideshow[\"Title\"]")]
  public void Evaluate_MissingKeysAndIndexesOutOfRange_FindNothing (string path)
  {
    Assert.True (condition: JsonValuePath.TryParse (text: path, path: out JsonValuePath? parsed, error: out _));
    Assert.False (parsed.TryEvaluate (root: JsonValuePathTests.Document, value: out _));
  }

  [Theory]
  [InlineData ("a..b",           "Expected a key at character 3.")]
  [InlineData ("a.",             "Expected a key at character 3.")]
  [InlineData ("$.",             "Expected a key at character 3.")]
  [InlineData ("a[",             "Expected an array index or a quoted key at character 3.")]
  [InlineData ("a[0",            "The '[' at character 2 isn't closed with ']'.")]
  [InlineData ("a[]",            "Expected an array index or a quoted key at character 3.")]
  [InlineData ("a[x]",           "Expected an array index or a quoted key at character 3.")]
  [InlineData ("a[-1]",          "Expected an array index or a quoted key at character 3.")]
  [InlineData ("a[1.5]",         "The '[' at character 2 isn't closed with ']'.")]
  [InlineData ("a[99999999999]", "The array index at character 3 is too large.")]
  [InlineData ("a[\"b]",         "The quoted key at character 2 never ends.")]
  [InlineData ("a[\"b\"",        "The '[' at character 2 isn't closed with ']'.")]
  [InlineData ("a[0]b",          "Unexpected 'b' at character 5; a key needs a '.' before it.")]
  [InlineData ("a[\"b\"]c",      "Unexpected 'c' at character 7; a key needs a '.' before it.")]
  [InlineData ("]",              "Unexpected ']' at character 1.")]
  public void TryParse_ExplainsBadSyntax (string path, string expected)
  {
    Assert.False (condition: JsonValuePath.TryParse (text: path, path: out JsonValuePath? parsed, error: out string? error));
    Assert.Null (parsed);
    Assert.Equal (expected: expected, actual: error);
  }

  private static JsonElement Find (string path) => JsonValuePathTests.Find (path: path, root: JsonValuePathTests.Document);

  private static JsonElement Find (string path, JsonElement root)
  {
    Assert.True (condition: JsonValuePath.TryParse (text: path, path: out JsonValuePath? parsed, error: out string? error),
                 userMessage: error);
    Assert.True (condition: parsed.TryEvaluate (root: root, value: out JsonElement value), userMessage: $"{path} wasn't found.");

    return value;
  }
}

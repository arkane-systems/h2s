using h2s.Panels;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="PanelOptionValues" />.
/// </summary>
public class PanelOptionValuesTests
{
  /// <summary>
  ///   A schema with one option of each type.
  /// </summary>
  private static readonly PanelOption[] Schema =
  [
    new (Key: "title", Label: "Title", Type: PanelOptionType.Text) { DefaultValue    = "Hello" },
    new (Key: "hours", Label: "Hours", Type: PanelOptionType.Integer) { Minimum      = 1, Maximum = 100 },
    new (Key: "limit", Label: "Limit", Type: PanelOptionType.Integer) { DefaultValue = 10 },
    new (Key: "show", Label: "Show", Type: PanelOptionType.Boolean) { DefaultValue   = true },
    new (Key: "plain", Label: "Plain", Type: PanelOptionType.Boolean),
  ];

  [Theory]
  [InlineData ((string?)null)]
  [InlineData ("")]
  [InlineData ("{}")]
  [InlineData ("not json")]
  [InlineData ("[1, 2]")]
  public void MissingOrUnreadableJson_ReadsAsDefaults (string? json)
  {
    var values = new PanelOptionValues (schema: PanelOptionValuesTests.Schema, json: json);

    Assert.Equal (expected: "Hello", actual: values.GetText ("title"));
    Assert.Null (values.GetInteger ("hours"));
    Assert.Equal (expected: 10, actual: values.GetInteger ("limit"));
    Assert.True (values.GetBoolean ("show"));
    Assert.False (values.GetBoolean ("plain"));
  }

  [Fact]
  public void StoredValues_OverrideDefaults ()
  {
    var values = new PanelOptionValues (schema: PanelOptionValuesTests.Schema,
                                        json: """{ "title": "", "hours": 48, "limit": null, "show": false, "extra": 1 }""");

    Assert.Equal (expected: "", actual: values.GetText ("title"));
    Assert.Equal (expected: 48, actual: values.GetInteger ("hours"));
    Assert.Null (values.GetInteger ("limit"));
    Assert.False (values.GetBoolean ("show"));
  }

  [Theory]
  [InlineData ("""{ "title": 5 }""")]
  [InlineData ("""{ "hours": "48" }""")]
  [InlineData ("""{ "hours": 0 }""")]
  [InlineData ("""{ "hours": 101 }""")]
  [InlineData ("""{ "hours": 1.5 }""")]
  [InlineData ("""{ "show": "false" }""")]
  public void ValuesThatDontFit_TakeTheDefault (string json)
  {
    var values = new PanelOptionValues (schema: PanelOptionValuesTests.Schema, json: json);

    Assert.Equal (expected: "Hello", actual: values.GetText ("title"));
    Assert.Null (values.GetInteger ("hours"));
    Assert.True (values.GetBoolean ("show"));
  }

  [Fact]
  public void AskingForAnUndeclaredOption_Throws ()
  {
    var values = new PanelOptionValues (schema: PanelOptionValuesTests.Schema, json: "{}");

    Assert.Throws<ArgumentException> (() => values.GetText ("missing"));
    Assert.Throws<ArgumentException> (() => values.GetBoolean ("title"));
  }
}

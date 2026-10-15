using h2s.Panels;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="PanelOptionForm" />, and how <see cref="PanelOptionValues" /> treats required options.
/// </summary>
public class PanelOptionFormTests
{
  /// <summary>
  ///   A schema with each kind of option.
  /// </summary>
  private static readonly PanelOption[] Schema =
  [
    new (Key: "title", Label: "Title", Type: PanelOptionType.Text) { IsRequired = true, DefaultValue = "Hello" },
    new (Key: "note", Label: "Note", Type: PanelOptionType.Text),
    new (Key: "hours", Label: "Hours", Type: PanelOptionType.Integer) { Minimum    = 1, Maximum         = 100 },
    new (Key: "limit", Label: "Limit", Type: PanelOptionType.Integer) { IsRequired = true, DefaultValue = 10 },
    new (Key: "show", Label: "Show", Type: PanelOptionType.Boolean) { DefaultValue = true },
  ];

  [Fact]
  public void TryParse_StoresValuesInSchemaOrder ()
  {
    Assert.True (PanelOptionForm.TryParse (schema: PanelOptionFormTests.Schema,
                                           posted: new Dictionary<string, string?>
                                                   {
                                                     ["show"]  = "false",
                                                     ["title"] = "  Main  ",
                                                     ["hours"] = "",
                                                     ["limit"] = " 7 ",
                                                     ["other"] = "ignored",
                                                   },
                                           json: out string? json,
                                           error: out _));
    Assert.Equal (expected: """{"title":"Main","note":"","hours":null,"limit":7,"show":false}""", actual: json);
  }

  [Fact]
  public void TryParse_TreatsAMissingBooleanAsOff ()
  {
    Assert.True (PanelOptionForm.TryParse (schema: PanelOptionFormTests.Schema,
                                           posted: new Dictionary<string, string?> { ["title"] = "T", ["limit"] = "1" },
                                           json: out string? json,
                                           error: out _));

    Assert.False (new PanelOptionValues (schema: PanelOptionFormTests.Schema, json: json).GetBoolean ("show"));
  }

  [Theory]
  [InlineData ("",  "5",    "",    "Title is required.")]
  [InlineData ("T", "",     "",    "Limit is required.")]
  [InlineData ("T", "five", "",    "Limit must be a whole number.")]
  [InlineData ("T", "1.5",  "",    "Limit must be a whole number.")]
  [InlineData ("T", "5",    "0",   "Hours must be at least 1.")]
  [InlineData ("T", "5",    "101", "Hours must be at most 100.")]
  public void TryParse_ReportsTheFirstProblem (string title, string limit, string hours, string expected)
  {
    Assert.False (PanelOptionForm.TryParse (schema: PanelOptionFormTests.Schema,
                                            posted: new Dictionary<string, string?>
                                                    {
                                                      ["title"] = title, ["limit"] = limit, ["hours"] = hours,
                                                    },
                                            json: out _,
                                            error: out string? error));
    Assert.Equal (expected: expected, actual: error);
  }

  [Fact]
  public void TryParse_LimitsTextLength ()
  {
    Assert.False (PanelOptionForm.TryParse (schema: PanelOptionFormTests.Schema,
                                            posted: new Dictionary<string, string?>
                                                    {
                                                      ["title"] = new (c: 'x', count: PanelOptionForm.MaxTextLength + 1),
                                                      ["limit"] = "1",
                                                    },
                                            json: out _,
                                            error: out string? error));
    Assert.Equal (expected: $"Title must be at most {PanelOptionForm.MaxTextLength} characters.", actual: error);
  }

  [Fact]
  public void Values_ReadBlankRequiredOptionsAsTheirDefaults ()
  {
    var values = new PanelOptionValues (schema: PanelOptionFormTests.Schema,
                                        json: """{ "title": " ", "note": "", "limit": null, "hours": null }""");

    Assert.Equal (expected: "Hello", actual: values.GetText ("title"));
    Assert.Equal (expected: "",      actual: values.GetText ("note"));
    Assert.Equal (expected: 10,      actual: values.GetInteger ("limit"));
    Assert.Null (values.GetInteger ("hours"));
    Assert.Equal (expected: ["title", "note", "hours", "limit", "show"], actual: values.Values.Keys);
  }
}

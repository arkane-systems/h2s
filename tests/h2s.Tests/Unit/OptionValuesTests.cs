using h2s.Widgets.Abstractions;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="OptionValues" />.
/// </summary>
public class OptionValuesTests
{
  /// <summary>
  ///   A schema with one option of each type.
  /// </summary>
  private static readonly SchemaOption[] Schema =
  [
    new (Key: "title", Label: "Title", Type: OptionType.Text) { DefaultValue    = "Hello" },
    new (Key: "hours", Label: "Hours", Type: OptionType.Integer) { Minimum      = 1, Maximum = 100 },
    new (Key: "limit", Label: "Limit", Type: OptionType.Integer) { DefaultValue = 10 },
    new (Key: "show", Label: "Show", Type: OptionType.Boolean) { DefaultValue   = true },
    new (Key: "plain", Label: "Plain", Type: OptionType.Boolean),
  ];

  /// <summary>
  ///   A schema with a URL, a patterned text option and a secret.
  /// </summary>
  private static readonly SchemaOption[] ConnectionSchema =
  [
    new (Key: "server", Label: "Server", Type: OptionType.Url) { DefaultValue = "https://default.home.arpa" },
    new (Key: "slug", Label: "Slug", Type: OptionType.Text) { Pattern         = "[a-z]+", DefaultValue = "main" },
    new (Key: "token", Label: "Token", Type: OptionType.Secret),
  ];

  [Theory]
  [InlineData ((string?)null)]
  [InlineData ("")]
  [InlineData ("{}")]
  [InlineData ("not json")]
  [InlineData ("[1, 2]")]
  public void MissingOrUnreadableJson_ReadsAsDefaults (string? json)
  {
    var values = new OptionValues (schema: OptionValuesTests.Schema, json: json);

    Assert.Equal (expected: "Hello", actual: values.GetText ("title"));
    Assert.Null (values.GetInteger ("hours"));
    Assert.Equal (expected: 10, actual: values.GetInteger ("limit"));
    Assert.True (values.GetBoolean ("show"));
    Assert.False (values.GetBoolean ("plain"));
  }

  [Fact]
  public void StoredValues_OverrideDefaults ()
  {
    var values = new OptionValues (schema: OptionValuesTests.Schema,
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
    var values = new OptionValues (schema: OptionValuesTests.Schema, json: json);

    Assert.Equal (expected: "Hello", actual: values.GetText ("title"));
    Assert.Null (values.GetInteger ("hours"));
    Assert.True (values.GetBoolean ("show"));
  }

  [Fact]
  public void AskingForAnUndeclaredOption_Throws ()
  {
    var values = new OptionValues (schema: OptionValuesTests.Schema, json: "{}");

    Assert.Throws<ArgumentException> (() => values.GetText ("missing"));
    Assert.Throws<ArgumentException> (() => values.GetBoolean ("title"));
  }

  [Theory]
  [InlineData ("""{ "server": "https://ha.home.arpa:8123", "slug": "home" }""", "https://ha.home.arpa:8123", "home")]
  [InlineData ("""{ "server": "", "slug": "" }""",                              "",                          "")]
  [InlineData ("""{ "server": "ftp://ha.home.arpa", "slug": "Home!" }""",       "https://default.home.arpa", "main")]
  [InlineData ("""{ "server": "https://me:pw@ha.home.arpa", "slug": 1 }""",     "https://default.home.arpa", "main")]
  [InlineData ("""{ "server": "ha.home.arpa" }""",                              "https://default.home.arpa", "main")]
  public void UrlsAndPatterns_ReadOnlyValuesThatFit (string json, string server, string slug)
  {
    var values = new OptionValues (schema: OptionValuesTests.ConnectionSchema, json: json);

    Assert.Equal (expected: server, actual: values.GetText ("server"));
    Assert.Equal (expected: slug,   actual: values.GetText ("slug"));
  }

  [Fact]
  public void Secrets_ComeFromTheSecretsAlone_AndStayOutOfTheValues ()
  {
    var values = new OptionValues (schema: OptionValuesTests.ConnectionSchema,
                                   json: """{ "token": "stored in the open" }""",
                                   secrets: new Dictionary<string, string> { ["token"] = "s3cret", ["slug"] = "ignored" });

    Assert.Equal (expected: "s3cret",           actual: values.GetSecret ("token"));
    Assert.Equal (expected: "main",             actual: values.GetText ("slug"));
    Assert.Equal (expected: ["server", "slug"], actual: values.Values.Keys);
    Assert.Throws<ArgumentException> (() => values.GetText ("token"));
    Assert.Throws<ArgumentException> (() => values.GetSecret ("slug"));
  }

  [Theory]
  [InlineData (null)]
  [InlineData (" ")]
  public void Secrets_ThatArentSet_ReadAsNull (string? secret)
    => Assert.Null (new OptionValues (schema: OptionValuesTests.ConnectionSchema,
                                      json: "{}",
                                      secrets: secret == null ? null : new Dictionary<string, string> { ["token"] = secret })
                     .GetSecret ("token"));
}

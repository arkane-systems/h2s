using h2s.Options;
using h2s.Widgets.Abstractions;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="OptionForm" />, and how <see cref="OptionValues" /> treats required options.
/// </summary>
public class OptionFormTests
{
  /// <summary>
  ///   A schema with each kind of option.
  /// </summary>
  private static readonly SchemaOption[] Schema =
  [
    new (Key: "title", Label: "Title", Type: OptionType.Text) { IsRequired = true, DefaultValue = "Hello" },
    new (Key: "note", Label: "Note", Type: OptionType.Text),
    new (Key: "hours", Label: "Hours", Type: OptionType.Integer) { Minimum    = 1, Maximum         = 100 },
    new (Key: "limit", Label: "Limit", Type: OptionType.Integer) { IsRequired = true, DefaultValue = 10 },
    new (Key: "show", Label: "Show", Type: OptionType.Boolean) { DefaultValue = true },
  ];

  [Fact]
  public void TryParse_StoresValuesInSchemaOrder ()
  {
    Assert.True (OptionForm.TryParse (schema: OptionFormTests.Schema,
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
    Assert.True (OptionForm.TryParse (schema: OptionFormTests.Schema,
                                      posted: new Dictionary<string, string?> { ["title"] = "T", ["limit"] = "1" },
                                      json: out string? json,
                                      error: out _));

    Assert.False (new OptionValues (schema: OptionFormTests.Schema, json: json).GetBoolean ("show"));
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
    Assert.False (OptionForm.TryParse (schema: OptionFormTests.Schema,
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
    Assert.False (OptionForm.TryParse (schema: OptionFormTests.Schema,
                                       posted: new Dictionary<string, string?>
                                               {
                                                 ["title"] = new (c: 'x', count: OptionForm.MaxTextLength + 1), ["limit"] = "1",
                                               },
                                       json: out _,
                                       error: out string? error));
    Assert.Equal (expected: $"Title must be at most {OptionForm.MaxTextLength} characters.", actual: error);
  }

  [Fact]
  public void Values_ReadBlankRequiredOptionsAsTheirDefaults ()
  {
    var values = new OptionValues (schema: OptionFormTests.Schema,
                                   json: """{ "title": " ", "note": "", "limit": null, "hours": null }""");

    Assert.Equal (expected: "Hello", actual: values.GetText ("title"));
    Assert.Equal (expected: "",      actual: values.GetText ("note"));
    Assert.Equal (expected: 10,      actual: values.GetInteger ("limit"));
    Assert.Null (values.GetInteger ("hours"));
    Assert.Equal (expected: ["title", "note", "hours", "limit", "show"], actual: values.Values.Keys);
  }

  [Theory]
  [InlineData ("https://ha.home.arpa:8123",  "home", null)]
  [InlineData ("",                           "",     null)]
  [InlineData ("ha.home.arpa",               "home", "Server must be an HTTP or HTTPS URL.")]
  [InlineData ("https://me:pw@ha.home.arpa", "home", "Server must be an HTTP or HTTPS URL.")]
  [InlineData ("https://ha.home.arpa",       "Home", "Slug may only contain lowercase letters.")]
  public void TryParse_ChecksUrlsAndPatterns (string server, string slug, string? error)
  {
    SchemaOption[] schema =
    [
      new (Key: "server", Label: "Server", Type: OptionType.Url),
      new (Key: "slug", Label: "Slug", Type: OptionType.Text)
      {
        Pattern = "[a-z]+", PatternDescription = "may only contain lowercase letters",
      },
      new (Key: "token", Label: "Token", Type: OptionType.Secret) { IsRequired = true },
    ];

    bool parsed = OptionForm.TryParse (schema: schema,
                                       posted: new Dictionary<string, string?>
                                               {
                                                 ["server"] = server, ["slug"] = slug, ["token"] = "s3cret",
                                               },
                                       json: out string? json,
                                       error: out string? actual);

    Assert.Equal (expected: error == null, actual: parsed);
    Assert.Equal (expected: error,         actual: actual);

    // Secrets are stored apart from the other values, so the form leaves them out.
    if (parsed)
    {
      Assert.Equal (expected: $$"""{"server":"{{server}}","slug":"{{slug}}"}""", actual: json);
    }
  }

  [Theory]
  [InlineData ("new", false, "old", "new")]
  [InlineData ("",    false, "old", "old")]
  [InlineData (" ",   false, null,  null)]
  [InlineData ("",    true,  "old", null)]
  [InlineData ("new", true,  "old", "new")]
  public void TryResolveSecrets_KeepsBlankSecrets_UnlessCleared (string typed, bool clear, string? stored, string? expected)
  {
    SchemaOption[] schema = [new (Key: "token", Label: "Token", Type: OptionType.Secret)];

    Assert.True (OptionForm.TryResolveSecrets (schema: schema,
                                               posted: new Dictionary<string, string?> { ["token"] = typed },
                                               clear: clear ? ["token"] : null,
                                               stored: stored == null
                                                         ? new Dictionary<string, string> ()
                                                         : new Dictionary<string, string> { ["token"] = stored },
                                               secrets: out Dictionary<string, string>? secrets,
                                               error: out _));
    Assert.Equal (expected: expected, actual: secrets.GetValueOrDefault ("token"));
  }

  [Theory]
  [InlineData ("",    null,  "Token is required.")]
  [InlineData ("",    "old", null)]
  [InlineData ("new", null,  null)]
  public void TryResolveSecrets_RequiresARequiredSecret_StoredOrTyped (string typed, string? stored, string? error)
  {
    SchemaOption[] schema = [new (Key: "token", Label: "Token", Type: OptionType.Secret) { IsRequired = true }];

    bool resolved = OptionForm.TryResolveSecrets (schema: schema,
                                                  posted: new Dictionary<string, string?> { ["token"] = typed },
                                                  clear: null,
                                                  stored: stored == null
                                                            ? new Dictionary<string, string> ()
                                                            : new Dictionary<string, string> { ["token"] = stored },
                                                  secrets: out _,
                                                  error: out string? actual);

    Assert.Equal (expected: error == null, actual: resolved);
    Assert.Equal (expected: error,         actual: actual);
  }

  [Fact]
  public void TryResolveSecrets_LimitsTheLength ()
    => Assert.False (OptionForm.TryResolveSecrets (schema:
                                                   [
                                                     new SchemaOption (Key: "token", Label: "Token", Type: OptionType.Secret),
                                                   ],
                                                   posted: new Dictionary<string, string?>
                                                           {
                                                             ["token"] = new (c: 'x', count: OptionForm.MaxSecretLength + 1),
                                                           },
                                                   clear: null,
                                                   stored: new Dictionary<string, string> (),
                                                   secrets: out _,
                                                   error: out _));
}

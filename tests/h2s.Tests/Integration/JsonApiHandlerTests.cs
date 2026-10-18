using System.Net;
using System.Text.Json;
using h2s.Integrations.JsonApi;
using h2s.Models;
using h2s.Tests.Infrastructure;
using h2s.Widgets.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the JSON API integration end to end: its widget kind in the link dialog, its widgets on links, and its
///   Settings card.
/// </summary>
public class JsonApiHandlerTests
{
  private const string SettingsPath = "/Admin/Settings";

  private const string Credential = "s3cret-api-t0ken";

  private const string SlidesWidget =
    """[{ "kind": "json-api/value", "options": { "path": "/json", "valuePath": "slideshow.slides", "name": "Slides", "unit": "slides" } }]""";

  [Theory]
  [InlineData ("tile")]
  [InlineData ("statusBar")]
  public async Task WidgetKinds_OffersTheValueKind_ForTilesAndStatusBars (string placement)
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddJsonApiAsync ();

    JsonElement kinds = await WidgetHandlerTests.GetJsonAsync (client: factory.CreateClient (),
                                                               query: $"handler=WidgetKinds&placement={placement}");
    JsonElement kind = Assert.Single (kinds.EnumerateArray ());

    Assert.Equal (expected: ("json-api/value", "JSON API: Value", "status", 60),
                  actual: (kind.GetProperty ("reference").GetString (), kind.GetProperty ("displayName").GetString (),
                           kind.GetProperty ("role").GetString (), kind.GetProperty ("defaultRefreshSeconds").GetInt32 ()));
    Assert.Equal (expected: ["path", "valuePath", "name", "unit", "expected"],
                  actual: kind.GetProperty ("options").EnumerateArray ().Select (o => o.GetProperty ("key").GetString ()));
  }

  [Fact]
  public async Task WidgetKinds_OffersNothing_BeforeTheConnectionExists ()
  {
    await using var factory = new DashboardApplicationFactory ();

    Assert.Empty ((await WidgetHandlerTests.GetJsonAsync (client: factory.CreateClient (), query: "handler=WidgetKinds"))
                 .EnumerateArray ());
  }

  [Fact]
  public async Task Links_SaveAJsonApiWidget_AndTheDashboardReadsIt ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeJsonApi };
    await factory.AddJsonApiAsync ();
    HttpClient client = factory.CreateClient ();

    int linkId = await WidgetHandlerTests.CreateLinkAsync (client: client,
                                                           factory: factory,
                                                           widgets: JsonApiHandlerTests.SlidesWidget);

    JsonElement widget = Assert.Single ((await WidgetHandlerTests.GetJsonAsync (client: client, query: $"handler=Link&id={linkId}"))
                                       .GetProperty ("widgets")
                                       .EnumerateArray ());

    Assert.Equal (expected: ("/json", "slideshow.slides", "Slides", "slides", ""),
                  actual: (widget.GetProperty ("values").GetProperty ("path").GetString (),
                           widget.GetProperty ("values").GetProperty ("valuePath").GetString (),
                           widget.GetProperty ("values").GetProperty ("name").GetString (),
                           widget.GetProperty ("values").GetProperty ("unit").GetString (),
                           widget.GetProperty ("values").GetProperty ("expected").GetString ()));

    int widgetId = widget.GetProperty ("id").GetInt32 ();

    using HttpResponseMessage response = await client.GetAsync (requestUri: $"/widgets/{widgetId}",
                                                                cancellationToken: TestContext.Current.CancellationToken);
    string html = await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Contains (expectedSubstring: "widget-ok",           actualString: html);
    Assert.Contains (expectedSubstring: "Slides is 2 slides.", actualString: html);
    Assert.Contains (expectedSubstring: ">2 slides<",          actualString: html);
  }

  [Theory]
  [InlineData ("""{ "path": "//evil.example/json", "valuePath": "a" }""",
               "Path must be a path under the base URL, not a full address.")]
  [InlineData ("""{ "path": "https://evil.example/json", "valuePath": "a" }""",
               "Path must be a path under the base URL, not a full address.")]
  [InlineData ("""{ "path": "/json" }""", "Value is required.")]
  public async Task Links_RefuseAWidgetWhosePathOrValueIsWrong (string options, string detail)
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddJsonApiAsync ();

    HttpResponseMessage response = await FormPoster.PostAsync (client: factory.CreateClient (),
                                                               pagePath: "/Admin/Editor",
                                                               handler: "CreateLink",
                                                               fields: await JsonApiHandlerTests.LinkFieldsAsync (factory: factory,
                                                                                                                  widgets:
                                                                                                                  $$"""[{ "kind": "json-api/value", "options": {{options}} }]"""));

    await FormPoster.AssertProblemAsync (response: response,
                                         status: HttpStatusCode.BadRequest,
                                         detail: $"Widget 1 (Value): {detail}");
  }

  [Fact]
  public async Task WidgetPreview_ReadsTheValue_BeforeTheWidgetIsSaved ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeJsonApi };
    await factory.AddJsonApiAsync ();

    string preview = await factory.CreateClient ()
                                  .GetStringAsync (requestUri:
                                                   "/Admin/Editor?handler=WidgetPreview&placement=tile&index=0&widgets=" +
                                                   Uri.EscapeDataString ("""[{ "kind": "json-api/value", "options": { "path": "/json", "valuePath": "slideshow.title", "name": "Title", "expected": "Sample Slide Show" } }]"""),
                                                   cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "widget-ok",                                actualString: preview);
    Assert.Contains (expectedSubstring: "Title is Sample Slide Show, as expected.", actualString: preview);
  }

  [Fact]
  public async Task SettingsCard_IsBuiltFromTheSchema ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: JsonApiHandlerTests.SettingsPath,
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """<section id="integration-json-api" """, actualString: html);

    JsonElement card = JsonApiHandlerTests.ReadCards (html).Single (c => c.GetProperty ("key").GetString () == "json-api");

    Assert.Equal (expected: "JSON API", actual: card.GetProperty ("displayName").GetString ());
    Assert.False (card.GetProperty ("isConfigured").GetBoolean ());
    Assert.Equal (expected: ["baseUrl", "auth", "username", "headerName", "credential"],
                  actual: card.GetProperty ("options").EnumerateArray ().Select (o => o.GetProperty ("key").GetString ()));
    Assert.Equal (expected: ["none", "basic", "bearer", "header"],
                  actual: card.GetProperty ("options")[1]
                              .GetProperty ("choices")
                              .EnumerateArray ()
                              .Select (c => c.GetProperty ("value").GetString ()));
  }

  [Fact]
  public async Task SaveConnection_StoresTheCredentialEncrypted_AndNeverSendsItBack ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeJsonApi };
    HttpClient      client  = factory.CreateClient ();

    JsonElement card = await JsonApiHandlerTests.PostOkAsync (client: client,
                                                              handler: "SaveConnection",
                                                              fields: JsonApiHandlerTests.Fields (credential: JsonApiHandlerTests
                                                                                                   .Credential));

    Assert.True (card.GetProperty ("isConfigured").GetBoolean ());
    Assert.True (card.GetProperty ("secretsSet").GetProperty ("credential").GetBoolean ());
    Assert.DoesNotContain (expectedSubstring: JsonApiHandlerTests.Credential, actualString: card.GetRawText ());

    string html = await client.GetStringAsync (requestUri: JsonApiHandlerTests.SettingsPath,
                                               cancellationToken: TestContext.Current.CancellationToken);

    Assert.DoesNotContain (expectedSubstring: JsonApiHandlerTests.Credential, actualString: html);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Connection connection = await context.Connections.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal (expected: JsonApiIntegration.IntegrationKey, actual: connection.IntegrationKey);
      Assert.DoesNotContain (expectedSubstring: JsonApiHandlerTests.Credential, actualString: connection.OptionsJson);
      Assert.DoesNotContain (expectedSubstring: JsonApiHandlerTests.Credential, actualString: connection.SecretsJson!);

      JsonApiSettings? stored =
        JsonApiSettings.From (new OptionValues (schema: new JsonApiIntegration ().ConnectionOptions, json: connection.OptionsJson));

      Assert.Equal (expected: (TestConnections.JsonApiBaseUrl, JsonApiAuth.Bearer), actual: (stored!.BaseUrl, stored.Auth));
    }

    // Saved again without it, the credential is kept.
    JsonElement kept =
      await JsonApiHandlerTests.PostOkAsync (client: client, handler: "SaveConnection", fields: JsonApiHandlerTests.Fields ());

    Assert.True (kept.GetProperty ("secretsSet").GetProperty ("credential").GetBoolean ());
  }

  [Fact]
  public async Task TestConnection_UsesTheStoredCredentialWhenLeftBlank_AndFailsWithout ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeJsonApi };
    HttpClient      client  = factory.CreateClient ();

    _ = await JsonApiHandlerTests.PostOkAsync (client: client,
                                               handler: "SaveConnection",
                                               fields: JsonApiHandlerTests.Fields (credential: TestConnections.JsonApiToken));

    JsonElement tested = await JsonApiHandlerTests.PostOkAsync (client: client,
                                                                handler: "TestConnection",
                                                                fields: JsonApiHandlerTests.Fields ());

    Assert.Equal (expected: "success", actual: tested.GetProperty ("level").GetString ());
    Assert.Equal (expected: "Connected. https://api.home.arpa answered 200 OK.",
                  actual: tested.GetProperty ("message").GetString ());

    JsonElement refused = await JsonApiHandlerTests.PostOkAsync (client: client,
                                                                 handler: "TestConnection",
                                                                 fields: JsonApiHandlerTests.Fields (credential: "wrong"));

    Assert.Equal (expected: "danger", actual: refused.GetProperty ("level").GetString ());
    Assert.Equal (expected: "https://api.home.arpa refused the credentials (401 Unauthorized).",
                  actual: refused.GetProperty ("message").GetString ());

    JsonElement cleared = await JsonApiHandlerTests.PostOkAsync (client: client,
                                                                 handler: "TestConnection",
                                                                 fields: JsonApiHandlerTests.Fields (clear: true));

    Assert.Equal (expected: "danger", actual: cleared.GetProperty ("level").GetString ());
    Assert.Contains (expectedSubstring: "needs a credential", actualString: cleared.GetProperty ("message").GetString ());
  }

  [Theory]
  [InlineData ("https://api.home.arpa/v1?key=1", JsonApiAuth.None,   "", "", JsonApiIntegration.InvalidBaseUrl)]
  [InlineData ("ftp://api.home.arpa",            JsonApiAuth.None,   "", "", "Base URL must be an HTTP or HTTPS URL.")]
  [InlineData ("https://api.home.arpa",          JsonApiAuth.Basic,  "", "", JsonApiIntegration.UsernameRequired)]
  [InlineData ("https://api.home.arpa",          JsonApiAuth.Header, "", "", JsonApiIntegration.HeaderNameRequired)]
  [InlineData ("https://api.home.arpa",
               JsonApiAuth.Header,
               "",
               "Bad Name",
               "Header name may only contain letters, numbers and the punctuation HTTP allows in a header name.")]
  [InlineData ("https://api.home.arpa",
               "digest",
               "",
               "",
               "Authentication must be one of None, Basic, Bearer token, Custom header.")]
  public async Task SaveConnection_RejectsInvalidValues (string baseUrl,
                                                         string auth,
                                                         string username,
                                                         string headerName,
                                                         string detail)
  {
    await using var factory = new DashboardApplicationFactory ();

    await FormPoster.AssertProblemAsync (response: await FormPoster.PostAsync (client: factory.CreateClient (),
                                                                               pagePath: JsonApiHandlerTests.SettingsPath,
                                                                               handler: "SaveConnection",
                                                                               fields: JsonApiHandlerTests.Fields (baseUrl: baseUrl,
                                                                                                                   auth: auth,
                                                                                                                   username:
                                                                                                                   username,
                                                                                                                   headerName:
                                                                                                                   headerName)),
                                         status: HttpStatusCode.BadRequest,
                                         detail: detail);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Assert.Empty (await context.Connections.ToListAsync (TestContext.Current.CancellationToken));
    }
  }

  [Fact]
  public async Task SaveConnection_AllowsNoCredential_ForOpenServices ()
  {
    await using var factory = new DashboardApplicationFactory ();

    JsonElement card = await JsonApiHandlerTests.PostOkAsync (client: factory.CreateClient (),
                                                              handler: "SaveConnection",
                                                              fields: JsonApiHandlerTests.Fields (auth: JsonApiAuth.None));

    Assert.True (card.GetProperty ("isConfigured").GetBoolean ());
    Assert.False (card.GetProperty ("secretsSet").GetProperty ("credential").GetBoolean ());
  }

  [Fact]
  public async Task RemoveConnection_DeletesItsWidgets_AndTheCardCountsThemFirst ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeJsonApi };
    await factory.AddJsonApiAsync ();
    HttpClient client = factory.CreateClient ();

    _ = await WidgetHandlerTests.CreateLinkAsync (client: client, factory: factory, widgets: JsonApiHandlerTests.SlidesWidget);

    string html = await client.GetStringAsync (requestUri: JsonApiHandlerTests.SettingsPath,
                                               cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: 1,
                  actual: JsonApiHandlerTests.ReadCards (html)
                                             .Single (c => c.GetProperty ("key").GetString () == "json-api")
                                             .GetProperty ("widgetCount")
                                             .GetInt32 ());

    JsonElement removed = await JsonApiHandlerTests.PostOkAsync (client: client,
                                                                 handler: "RemoveConnection",
                                                                 fields:
                                                                 [
                                                                   new KeyValuePair<string, string> (key: "integration",
                                                                                                     value: "json-api"),
                                                                 ]);

    Assert.False (removed.GetProperty ("isConfigured").GetBoolean ());

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Assert.Empty (await context.Widgets.ToListAsync (TestContext.Current.CancellationToken));
    }
  }

  /// <summary>
  ///   Builds the fields the JSON API card posts.
  /// </summary>
  /// <param name="baseUrl">The base URL.</param>
  /// <param name="auth">The authentication method.</param>
  /// <param name="username">The username.</param>
  /// <param name="headerName">The header name.</param>
  /// <param name="credential">A newly typed credential, if any.</param>
  /// <param name="clear">Whether to clear the stored credential.</param>
  /// <returns>The fields.</returns>
  private static KeyValuePair<string, string>[] Fields (string  baseUrl    = TestConnections.JsonApiBaseUrl,
                                                        string  auth       = JsonApiAuth.Bearer,
                                                        string  username   = "",
                                                        string  headerName = "",
                                                        string? credential = null,
                                                        bool    clear      = false)
    =>
    [
      new (key: "integration", value: JsonApiIntegration.IntegrationKey),
      new (key: "options[baseUrl]", value: baseUrl),
      new (key: "options[auth]", value: auth),
      new (key: "options[username]", value: username),
      new (key: "options[headerName]", value: headerName),
      .. credential == null ? [] : new[] { new KeyValuePair<string, string> (key: "secrets[credential]", value: credential) },
      .. clear ? new[] { new KeyValuePair<string, string> (key: "clearSecrets", value: "credential") } : [],
    ];

  /// <summary>
  ///   Builds the fields of a new link in the first category, with widgets.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="widgets">The widgets as JSON.</param>
  /// <returns>The fields.</returns>
  private static async Task<KeyValuePair<string, string>[]> LinkFieldsAsync (DashboardApplicationFactory factory, string widgets)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      int categoryId = await context.Categories.Where (c => c.Name == "Media")
                                    .Select (c => c.Id)
                                    .SingleAsync (TestContext.Current.CancellationToken);

      return
      [
        new KeyValuePair<string, string> (key: "categoryId", value: categoryId.ToString ()),
        new KeyValuePair<string, string> (key: "label",      value: "API"),
        new KeyValuePair<string, string> (key: "url",        value: "https://api.home.arpa"),
        new KeyValuePair<string, string> (key: "widgets",    value: widgets),
      ];
    }
  }

  /// <summary>
  ///   Posts to a Settings handler and reads its successful JSON answer.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="handler">The handler.</param>
  /// <param name="fields">The form fields.</param>
  /// <returns>The answer.</returns>
  private static async Task<JsonElement> PostOkAsync (HttpClient client, string handler, KeyValuePair<string, string>[] fields)
  {
    HttpResponseMessage response = await FormPoster.PostAsync (client: client,
                                                               pagePath: JsonApiHandlerTests.SettingsPath,
                                                               handler: handler,
                                                               fields: fields);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return await FormPoster.ReadJsonAsync (response);
  }

  /// <summary>
  ///   Reads the integration cards the Settings page embeds.
  /// </summary>
  /// <param name="html">The page.</param>
  /// <returns>The cards.</returns>
  private static List<JsonElement> ReadCards (string html)
  {
    const string Start = """<script type="application/json" id="integrations-data">""";

    int    start = html.IndexOf (value: Start, comparisonType: StringComparison.Ordinal) + Start.Length;
    string json  = html[start..html.IndexOf (value: "</script>", startIndex: start, comparisonType: StringComparison.Ordinal)];

    return [.. JsonDocument.Parse (json).RootElement.Clone ().EnumerateArray ()];
  }
}

using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using h2s.Integrations.JsonApi;
using h2s.Services;
using h2s.Tests.Infrastructure;
using h2s.Widgets.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for the JSON API integration: its value widget, connection rules and connection test.
/// </summary>
public class JsonApiTests
{
  private static readonly JsonApiValueWidgetKind Value = new ();

  private static readonly JsonApiIntegration Integration = new ();

  [Fact]
  public async Task Value_AnArrayShowsItsCount_WithItsUnit_InThePill ()
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync ("""{"path":"/json","valuePath":"slideshow.slides","name":"Slides","unit":"slides"}""");

    Assert.Equal (expected: WidgetStatus.Ok,   actual: result.Status);
    Assert.Equal (expected: ["2 slides"],      actual: result.Stats.Select (s => s.Text));
    Assert.Equal (expected: "Slides is 2 slides.", actual: result.Description);
  }

  [Theory]
  [InlineData ("""{"path":"/json","valuePath":"slideshow.slides"}""",                      "2 items", "slideshow.slides is 2 items.")]
  [InlineData ("""{"path":"/json","valuePath":"slideshow.slides[1].items"}""",             "2 items", "slideshow.slides[1].items is 2 items.")]
  [InlineData ("""{"path":"/json","valuePath":"slideshow.title"}""",                       "Sample Slide Show", "slideshow.title is Sample Slide Show.")]
  [InlineData ("""{"path":"/json","valuePath":"slideshow.title","name":"Title"}""",         "Sample Slide Show", "Title is Sample Slide Show.")]
  public async Task Value_ShowsStringsAndCounts (string optionsJson, string pill, string description)
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (optionsJson);

    Assert.Equal (expected: WidgetStatus.Ok, actual: result.Status);
    Assert.Equal (expected: [pill],          actual: result.Stats.Select (s => s.Text));
    Assert.Equal (expected: description,     actual: result.Description);
  }

  [Theory]
  [InlineData ("""{"latency":42}""",       """{"unit":"ms"}""", "42 ms")]
  [InlineData ("""{"latency":42.50}""",    """{"unit":"ms"}""", "42.50 ms")]
  [InlineData ("""{"latency":93}""",       """{"unit":"%"}""", "93%")]
  [InlineData ("""{"latency":-1.5e3}""",   """{}""",            "-1.5e3")]
  [InlineData ("""{"latency":true}""",     """{}""",            "true")]
  [InlineData ("""{"latency":false}""",    """{}""",            "false")]
  [InlineData ("""{"latency":[]}""",       """{}""",            "0 items")]
  [InlineData ("""{"latency":[1]}""",      """{}""",            "1 item")]
  [InlineData ("""{"latency":[1,2,3]}""",  """{"unit":"jobs"}""", "3 jobs")]
  public async Task Value_ShowsNumbersAndBooleans_WithTheUnitAttachedToThePillText (string body, string extra, string pill)
  {
    await using var api = new JsonApiFixture (_ => JsonApiFixture.Json (body));

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "latency", extra: extra));

    Assert.Equal (expected: WidgetStatus.Ok, actual: result.Status);
    Assert.Equal (expected: [pill],          actual: result.Stats.Select (s => s.Text));
  }

  [Fact]
  public async Task Value_CutsALongStringWithAnEllipsis ()
  {
    var text = new string ('a', 60);
    await using var api = new JsonApiFixture (_ => JsonApiFixture.Json ($$"""{"v":"{{text}}"}"""));

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "v"));

    Assert.Equal (expected: $"{new string ('a', 40)}…", actual: Assert.Single (result.Stats).Value);

    // The comparison with an expected value uses the whole string.
    WidgetResult expected = await api.FetchAsync (JsonApiTests.Options (valuePath: "v", extra: $$"""{"expected":"{{text}}"}"""));

    Assert.Equal (expected: WidgetStatus.Ok, actual: expected.Status);
  }

  [Theory]
  [InlineData ("healthy",  WidgetStatus.Ok,    "Status is healthy, as expected.")]
  [InlineData ("degraded", WidgetStatus.Error, "Status is degraded, not healthy.")]
  [InlineData ("Healthy",  WidgetStatus.Error, "Status is Healthy, not healthy.")]
  public async Task Value_ComparesWithTheExpectedValue (string actual, WidgetStatus status, string description)
  {
    await using var api = new JsonApiFixture (_ => JsonApiFixture.Json ($$"""{"status":"{{actual}}"}"""));

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "status",
                                                                       extra: """{"name":"Status","expected":" healthy "}"""));

    Assert.Equal (expected: status,      actual: result.Status);
    Assert.Equal (expected: description, actual: result.Description);
    Assert.Equal (expected: actual,      actual: Assert.Single (result.Stats).Value);
  }

  [Theory]
  [InlineData ("""{"a":{"b":1}}""", "a", "a is an object, not a single value; point the value path at one of its keys.")]
  [InlineData ("""{"a":null}""",    "a", "a is null.")]
  public async Task Value_AnObjectOrNull_IsAWarning (string body, string valuePath, string description)
  {
    await using var api = new JsonApiFixture (_ => JsonApiFixture.Json (body));

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: valuePath));

    Assert.Equal (expected: WidgetStatus.Warning, actual: result.Status);
    Assert.Empty (result.Stats);
    Assert.Equal (expected: description, actual: result.Description);
  }

  [Theory]
  [InlineData ("slideshow.nothing", "slideshow.nothing not found in the response.")]
  [InlineData ("slideshow.slides[2]", "slideshow.slides[2] not found in the response.")]
  public async Task Value_AMissingPath_IsAWarning (string valuePath, string description)
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: valuePath));

    Assert.Equal (expected: WidgetStatus.Warning, actual: result.Status);
    Assert.Equal (expected: description,          actual: result.Description);

    WidgetResult named = await api.FetchAsync (JsonApiTests.Options (valuePath: valuePath, extra: """{"name":"Slides"}"""));

    Assert.Equal (expected: "Slides not found in the response.", actual: named.Description);
  }

  [Fact]
  public async Task Value_AnErrorStatus_IsAnError ()
  {
    await using var api = new JsonApiFixture (_ => new HttpResponseMessage (HttpStatusCode.ServiceUnavailable));

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "a"));

    Assert.Equal (expected: WidgetStatus.Error, actual: result.Status);
    Assert.Equal (expected: "The API answered 503 Service Unavailable for /json.", actual: result.Description);
  }

  [Fact]
  public async Task Value_SomethingThatIsntJson_IsAnError ()
  {
    await using var api = new JsonApiFixture (_ => new HttpResponseMessage (HttpStatusCode.OK)
                                                   {
                                                     Content = new StringContent ("<html>Welcome</html>"),
                                                   });

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "a"));

    Assert.Equal (expected: WidgetStatus.Error, actual: result.Status);
    Assert.Equal (expected: "The answer for /json isn't JSON.", actual: result.Description);
  }

  [Fact]
  public async Task Value_AnEmptyAnswer_IsNotJson ()
  {
    await using var api = new JsonApiFixture (_ => new HttpResponseMessage (HttpStatusCode.NoContent));

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "a"));

    Assert.Equal (expected: WidgetStatus.Error, actual: result.Status);
  }

  [Theory]
  [InlineData (false)]
  [InlineData (true)]
  public async Task Value_AnAnswerOverTheLimit_IsAnError_AndNotAnException (bool lengthKnown)
  {
    var big = $$"""{"a":"{{new string ('x', JsonApiClient.MaxBodyBytes)}}"}""";
    await using var api = new JsonApiFixture (_ => new HttpResponseMessage (HttpStatusCode.OK)
                                                   {
                                                     Content = lengthKnown
                                                                 ? new StringContent (big)
                                                                 : new UnknownLengthContent (Encoding.UTF8.GetBytes (big)),
                                                   });

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "a"));

    Assert.Equal (expected: WidgetStatus.Error, actual: result.Status);
    Assert.Equal (expected: "The answer for /json is larger than 256 KB.", actual: result.Description);
  }

  [Fact]
  public async Task Value_AnAnswerJustWithinTheLimit_IsRead ()
  {
    string padding = new (' ', JsonApiClient.MaxBodyBytes - """{"a":1}""".Length);
    await using var api = new JsonApiFixture (_ => new HttpResponseMessage (HttpStatusCode.OK)
                                                   {
                                                     Content = new UnknownLengthContent (Encoding.UTF8.GetBytes ($$"""{"a":1}{{padding}}""")),
                                                   });

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "a"));

    Assert.Equal (expected: ["1"], actual: result.Stats.Select (s => s.Text));
  }

  [Fact]
  public async Task Value_AServiceThatCantBeReached_IsUnknown ()
  {
    await using var api = new JsonApiFixture (_ => throw new HttpRequestException ("Connection refused"));

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "a"));

    Assert.Equal (expected: WidgetStatus.Unknown, actual: result.Status);
    Assert.Equal (expected: "Couldn't reach the JSON API at api.home.arpa.", actual: result.Description);
  }

  [Theory]
  [InlineData (JsonApiAuth.None,   null,       null,        null,      null,                 null,                null)]
  [InlineData (JsonApiAuth.Basic,  "alice",    null,        "s3cret",  "Basic YWxpY2U6czNjcmV0", null,              null)]
  [InlineData (JsonApiAuth.Bearer, null,       null,        "tok-123", "Bearer tok-123",     null,                null)]
  [InlineData (JsonApiAuth.Header, null,       "X-Api-Key", "key-456", null,                 "X-Api-Key",         "key-456")]
  public async Task Value_SendsTheConnectionsAuthentication (string  auth,
                                                             string? username,
                                                             string? headerName,
                                                             string? credential,
                                                             string? authorization,
                                                             string? header,
                                                             string? headerValue)
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (optionsJson: JsonApiTests.Options (valuePath: "slideshow.title"),
                                                connectionJson: JsonApiFixture.Connection (auth: auth,
                                                                                          username: username ?? "",
                                                                                          headerName: headerName ?? ""),
                                                credential: credential);

    Assert.Equal (expected: WidgetStatus.Ok, actual: result.Status);

    HttpRequestMessage request = Assert.Single (api.Handler.Requests);

    Assert.Equal (expected: HttpMethod.Get,          actual: request.Method);
    Assert.Equal (expected: "application/json",      actual: request.Headers.Accept.ToString ());
    Assert.Equal (expected: authorization,           actual: request.Headers.Authorization?.ToString ());
    Assert.Equal (expected: headerValue,             actual: header == null ? null : Assert.Single (request.Headers.GetValues (header)));

    if (auth is JsonApiAuth.None or JsonApiAuth.Header)
    {
      Assert.Null (request.Headers.Authorization);
    }
  }

  [Theory]
  [InlineData (JsonApiAuth.Bearer)]
  [InlineData (JsonApiAuth.Basic)]
  public async Task Value_WithoutItsCredential_IsUnknown_AndSendsNothing (string auth)
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (optionsJson: JsonApiTests.Options (valuePath: "slideshow.title"),
                                                connectionJson: JsonApiFixture.Connection (auth: auth, username: "alice"),
                                                credential: null);

    Assert.Equal (expected: WidgetStatus.Unknown, actual: result.Status);
    Assert.Equal (expected: "The JSON API connection needs a credential; add one in Settings → Integrations.", actual: result.Description);
    Assert.Empty (api.Handler.Requests);
  }

  [Fact]
  public async Task Value_ACredentialWithANewline_IsNeverSent ()
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (optionsJson: JsonApiTests.Options (valuePath: "slideshow.title"),
                                                connectionJson: JsonApiFixture.Connection (auth: JsonApiAuth.Bearer),
                                                credential: "abc\r\nX-Evil: 1");

    Assert.Equal (expected: WidgetStatus.Unknown, actual: result.Status);
    Assert.Empty (api.Handler.Requests);
  }

  [Theory]
  [InlineData ("https://evil.example/json")]
  [InlineData ("http://evil.example")]
  [InlineData ("//evil.example/json")]
  [InlineData ("mailto:me@example.com")]
  [InlineData ("javascript:alert(1)")]
  [InlineData ("\\\\evil.example\\json")]
  [InlineData ("/json#fragment")]
  [InlineData ("")]
  public async Task Value_RefusesAnAbsolutePath_WithoutSendingARequest (string path)
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "slideshow.title", path: path));

    Assert.Equal (expected: WidgetStatus.Unknown, actual: result.Status);
    Assert.Empty (api.Handler.Requests);
  }

  [Theory]
  [InlineData ("/../other")]
  [InlineData ("..")]
  [InlineData ("../other")]
  [InlineData ("/json/../../other")]
  [InlineData ("/%2e%2e/other")]
  [InlineData ("/%2E%2E/other")]
  [InlineData ("/.%2e/other")]
  public async Task Value_RefusesAPathThatLeavesTheBasesPrefix_WithoutSendingARequest (string path)
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (optionsJson: JsonApiTests.Options (valuePath: "slideshow.title", path: path),
                                                connectionJson: JsonApiFixture.Connection (baseUrl: "https://api.home.arpa/v1"));

    Assert.Equal (expected: WidgetStatus.Unknown, actual: result.Status);
    Assert.Equal (expected: JsonApiUrls.LeavesBase, actual: result.Description);
    Assert.Empty (api.Handler.Requests);
  }

  [Theory]
  [InlineData ("https://api.home.arpa",     "/json",           "https://api.home.arpa/json")]
  [InlineData ("https://api.home.arpa/",    "json",            "https://api.home.arpa/json")]
  [InlineData ("https://api.home.arpa/api", "/json",           "https://api.home.arpa/api/json")]
  [InlineData ("https://api.home.arpa/api", "status?full=1",   "https://api.home.arpa/api/status?full=1")]
  [InlineData ("https://api.home.arpa/api/", "/a/b?x=1&y=2",   "https://api.home.arpa/api/a/b?x=1&y=2")]
  [InlineData ("http://api.home.arpa:8080/api", "/json",       "http://api.home.arpa:8080/api/json")]
  [InlineData ("https://api.home.arpa/api", "/a/../json",      "https://api.home.arpa/api/json")]
  [InlineData ("https://api.home.arpa/api", "/",               "https://api.home.arpa/api/")]
  public async Task Value_JoinsThePathToTheBase_KeepingAPathPrefix (string baseUrl, string path, string requested)
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (optionsJson: JsonApiTests.Options (valuePath: "slideshow.title", path: path),
                                                connectionJson: JsonApiFixture.Connection (baseUrl: baseUrl));

    Assert.Equal (expected: WidgetStatus.Ok, actual: result.Status);
    Assert.Equal (expected: requested,       actual: Assert.Single (api.Handler.Requests).RequestUri!.AbsoluteUri);
  }

  [Theory]
  [InlineData ("https://evil.example/json")]
  [InlineData ("//evil.example/json")]
  [InlineData ("http:evil")]
  [InlineData ("\\\\evil.example\\json")]
  [InlineData ("/json#fragment")]
  [InlineData ("/json with spaces")]
  [InlineData ("")]
  public void Resolve_RefusesAnythingButARelativePath (string path)
  {
    Assert.False (JsonApiUrls.TryResolve (baseUrl: "https://api.home.arpa/v1", path: path, url: out Uri? url, error: out string? error));
    Assert.Null (url);
    Assert.Equal (expected: JsonApiUrls.NotRelative, actual: error);
  }

  [Fact]
  public async Task Value_AnInvalidValuePath_IsUnknown_WithoutSendingARequest ()
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (JsonApiTests.Options (valuePath: "slideshow.slides[x]"));

    Assert.Equal (expected: WidgetStatus.Unknown, actual: result.Status);
    Assert.Equal (expected: "The value path isn't valid: Expected an array index or a quoted key at character 18.",
                  actual: result.Description);
    Assert.Empty (api.Handler.Requests);
  }

  [Fact]
  public async Task Value_WidgetsOnOneUrl_ShareOneRequest ()
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult title  = await api.FetchAsync (JsonApiTests.Options (valuePath: "slideshow.title"));
    WidgetResult slides = await api.FetchAsync (JsonApiTests.Options (valuePath: "slideshow.slides", extra: """{"unit":"slides"}"""));

    Assert.Equal (expected: "Sample Slide Show", actual: Assert.Single (title.Stats).Text);
    Assert.Equal (expected: "2 slides",           actual: Assert.Single (slides.Stats).Text);
    Assert.Single (api.Handler.Requests);

    // Another path, or another credential, is another request.
    _ = await api.FetchAsync (JsonApiTests.Options (valuePath: "slideshow.title", path: "/json?x=1"));

    Assert.Equal (expected: 2, actual: api.Handler.Requests.Count);

    _ = await api.FetchAsync (optionsJson: JsonApiTests.Options (valuePath: "slideshow.title"),
                              connectionJson: JsonApiFixture.Connection (auth: JsonApiAuth.Bearer),
                              credential: "token");

    Assert.Equal (expected: 3, actual: api.Handler.Requests.Count);
  }

  [Fact]
  public async Task Value_ErrorsAreNotCached ()
  {
    var  calls = 0;
    await using var api = new JsonApiFixture (_ => ++calls == 1
                                                     ? new HttpResponseMessage (HttpStatusCode.BadGateway)
                                                     : JsonApiFixture.Json (TestConnections.HttpbinJson));

    WidgetResult failed = await api.FetchAsync (JsonApiTests.Options (valuePath: "slideshow.title"));
    WidgetResult fixedUp = await api.FetchAsync (JsonApiTests.Options (valuePath: "slideshow.title"));

    Assert.Equal (expected: WidgetStatus.Error, actual: failed.Status);
    Assert.Equal (expected: WidgetStatus.Ok,    actual: fixedUp.Status);
  }

  [Fact]
  public async Task Value_WithoutAConnectionBaseUrl_IsUnknown ()
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    WidgetResult result = await api.FetchAsync (optionsJson: JsonApiTests.Options (valuePath: "a"), connectionJson: "{}");

    Assert.Equal (expected: WidgetStatus.Unknown, actual: result.Status);
    Assert.Equal (expected: "The JSON API isn't set up.", actual: result.Description);
  }

  [Fact]
  public void Value_IsAStatusWidget_ForTilesAndStatusBars ()
  {
    Assert.Equal (expected: WidgetRole.Status, actual: JsonApiTests.Value.Role);
    Assert.Equal (expected: WidgetPlacements.Tile | WidgetPlacements.StatusBar, actual: ((IWidgetKind)JsonApiTests.Value).Placements);
    Assert.Equal (expected: TimeSpan.FromSeconds (60), actual: JsonApiTests.Value.DefaultRefresh);
    Assert.Equal (expected: "json-api", actual: JsonApiTests.Integration.Key);
    Assert.False (JsonApiTests.Integration.AllowsMultipleConnections);
  }

  [Theory]
  [InlineData ("/json",           true)]
  [InlineData ("status?full=1",   true)]
  [InlineData ("a/b:c",           true)]
  [InlineData ("//evil.example",  false)]
  [InlineData ("https://evil",    false)]
  [InlineData ("http:evil",       false)]
  public void ThePathOption_RefusesAddressesAtFormTime (string path, bool allowed)
  {
    SchemaOption option = JsonApiTests.Value.Options.Single (o => o.Key == JsonApiValueWidgetKind.PathOption);

    Assert.Equal (expected: allowed, actual: OptionValues.MatchesPattern (option: option, value: path));
  }

  [Theory]
  [InlineData ("X-Api-Key",  true)]
  [InlineData ("Authorization", true)]
  [InlineData ("x_token.1",  true)]
  [InlineData ("Bad Header", false)]
  [InlineData ("X-Api:Key",  false)]
  [InlineData ("X\r\nEvil",  false)]
  public void TheHeaderNameOption_AcceptsHttpTokens (string name, bool allowed)
  {
    SchemaOption option = JsonApiTests.Integration.ConnectionOptions.Single (o => o.Key == JsonApiIntegration.HeaderNameOption);

    Assert.Equal (expected: allowed, actual: OptionValues.MatchesPattern (option: option, value: name));
  }

  [Theory]
  [InlineData ("https://api.home.arpa",              JsonApiAuth.None,   "",      "",          null)]
  [InlineData ("http://api.home.arpa:8080/api/v1/",  JsonApiAuth.Bearer, "",      "",          null)]
  [InlineData ("https://api.home.arpa",              JsonApiAuth.Basic,  "alice", "",          null)]
  [InlineData ("https://api.home.arpa",              JsonApiAuth.Header, "",      "X-Api-Key", null)]
  [InlineData ("https://api.home.arpa/api?key=1",    JsonApiAuth.None,   "",      "",          JsonApiIntegration.InvalidBaseUrl)]
  [InlineData ("https://api.home.arpa/api#top",      JsonApiAuth.None,   "",      "",          JsonApiIntegration.InvalidBaseUrl)]
  [InlineData ("https://api.home.arpa/api?",         JsonApiAuth.None,   "",      "",          JsonApiIntegration.InvalidBaseUrl)]
  [InlineData ("https://user:pw@api.home.arpa",      JsonApiAuth.None,   "",      "",          JsonApiIntegration.InvalidBaseUrl)]
  [InlineData ("ftp://api.home.arpa",                JsonApiAuth.None,   "",      "",          JsonApiIntegration.InvalidBaseUrl)]
  [InlineData ("api.home.arpa",                      JsonApiAuth.None,   "",      "",          JsonApiIntegration.InvalidBaseUrl)]
  [InlineData ("https://api.home.arpa",              JsonApiAuth.Basic,  "",      "",          JsonApiIntegration.UsernameRequired)]
  [InlineData ("https://api.home.arpa",              JsonApiAuth.Header, "",      "",          JsonApiIntegration.HeaderNameRequired)]
  public void Validate_ChecksTheBaseUrl_AndWhatEachAuthMethodNeeds (string  baseUrl,
                                                                   string  auth,
                                                                   string  username,
                                                                   string  headerName,
                                                                   string? expected)
  {
    var options = new OptionValues (schema: JsonApiTests.Integration.ConnectionOptions,
                                     json: JsonApiFixture.Connection (baseUrl: baseUrl, auth: auth, username: username, headerName: headerName));

    // A stored value that doesn't fit its option reads as blank, so a bad base URL arrives here as nothing at all.
    Assert.Equal (expected: expected, actual: JsonApiTests.Integration.Validate (options));
  }

  [Fact]
  public void Validate_DoesNotNeedTheCredential_SinceImportsNeverCarryIt ()
  {
    var options = new OptionValues (schema: JsonApiTests.Integration.ConnectionOptions,
                                     json: JsonApiFixture.Connection (auth: JsonApiAuth.Bearer));

    Assert.Null (JsonApiTests.Integration.Validate (options));
  }

  [Fact]
  public void Settings_KeepTheCredentialOutOfToString ()
  {
    var options = new OptionValues (schema: JsonApiTests.Integration.ConnectionOptions,
                                     json: JsonApiFixture.Connection (auth: JsonApiAuth.Bearer),
                                     secrets: new Dictionary<string, string> { [JsonApiIntegration.CredentialOption] = "very-secret" });

    JsonApiSettings settings = JsonApiSettings.From (options)!;

    Assert.Equal (expected: "very-secret", actual: settings.Credential);
    Assert.DoesNotContain (expectedSubstring: "very-secret", actualString: settings.ToString ());
    Assert.Null (JsonApiSettings.From (new OptionValues (schema: JsonApiTests.Integration.ConnectionOptions, json: "{}")));
  }

  [Fact]
  public async Task Test_ASuccessfulRequestForTheBase_Succeeds ()
  {
    await using var api = new JsonApiFixture (_ => new HttpResponseMessage (HttpStatusCode.OK));

    ConnectionTestResult result = await api.TestAsync (JsonApiFixture.Connection ());

    Assert.Equal (expected: ConnectionTestLevel.Success, actual: result.Level);
    Assert.Equal (expected: "Connected. https://api.home.arpa answered 200 OK.", actual: result.Message);
    Assert.Equal (expected: "https://api.home.arpa/", actual: Assert.Single (api.Handler.Requests).RequestUri!.AbsoluteUri);
  }

  [Fact]
  public async Task Test_SendsTheAuthentication_AndIsNeverServedFromTheCache ()
  {
    await using var api = new JsonApiFixture (TestConnections.ServeJsonApi);

    ConnectionTestResult first  = await api.TestAsync (connectionJson: JsonApiFixture.Connection (auth: JsonApiAuth.Bearer),
                                                      credential: TestConnections.JsonApiToken);
    ConnectionTestResult second = await api.TestAsync (connectionJson: JsonApiFixture.Connection (auth: JsonApiAuth.Bearer),
                                                      credential: TestConnections.JsonApiToken);

    Assert.Equal (expected: ConnectionTestLevel.Success, actual: first.Level);
    Assert.Equal (expected: ConnectionTestLevel.Success, actual: second.Level);
    Assert.Equal (expected: 2, actual: api.Handler.Requests.Count);
  }

  [Theory]
  [InlineData (HttpStatusCode.Unauthorized, JsonApiAuth.Bearer, "https://api.home.arpa refused the credentials (401 Unauthorized).")]
  [InlineData (HttpStatusCode.Forbidden,    JsonApiAuth.Bearer, "https://api.home.arpa refused the credentials (403 Forbidden).")]
  [InlineData (HttpStatusCode.Unauthorized, JsonApiAuth.None,
               "https://api.home.arpa answered 401 Unauthorized: it asks for credentials, so choose an authentication method.")]
  public async Task Test_RefusedCredentials_AreDanger (HttpStatusCode status, string auth, string message)
  {
    await using var api = new JsonApiFixture (_ => new HttpResponseMessage (status));

    ConnectionTestResult result = await api.TestAsync (connectionJson: JsonApiFixture.Connection (auth: auth), credential: "token");

    Assert.Equal (expected: ConnectionTestLevel.Danger, actual: result.Level);
    Assert.Equal (expected: message,                    actual: result.Message);
  }

  [Theory]
  [InlineData (HttpStatusCode.NotFound,            "404 Not Found")]
  [InlineData (HttpStatusCode.InternalServerError, "500 Internal Server Error")]
  public async Task Test_AnyOtherStatus_IsAWarningThatNamesIt (HttpStatusCode status, string named)
  {
    await using var api = new JsonApiFixture (_ => new HttpResponseMessage (status));

    ConnectionTestResult result = await api.TestAsync (JsonApiFixture.Connection ());

    Assert.Equal (expected: ConnectionTestLevel.Warning, actual: result.Level);
    Assert.Contains (expectedSubstring: $"answered {named}.", actualString: result.Message);
    Assert.Contains (expectedSubstring: "Widgets read paths under the base URL", actualString: result.Message);
  }

  [Fact]
  public async Task Test_ACredentialThatIsMissing_IsDanger_WithoutARequest ()
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    ConnectionTestResult result = await api.TestAsync (JsonApiFixture.Connection (auth: JsonApiAuth.Bearer));

    Assert.Equal (expected: ConnectionTestLevel.Danger, actual: result.Level);
    Assert.Equal (expected: "The JSON API connection needs a credential; add one in Settings → Integrations.", actual: result.Message);
    Assert.Empty (api.Handler.Requests);
  }

  [Fact]
  public async Task Test_AServerThatCantBeReached_IsDanger ()
  {
    await using var api = new JsonApiFixture (_ => throw new HttpRequestException ("Connection refused"));

    ConnectionTestResult result = await api.TestAsync (JsonApiFixture.Connection ());

    Assert.Equal (expected: ConnectionTestLevel.Danger, actual: result.Level);
    Assert.Equal (expected: "Couldn't reach https://api.home.arpa.", actual: result.Message);
  }

  [Fact]
  public async Task Test_AnUntrustedCertificate_IsAWarning ()
  {
    // Every strict request fails the TLS handshake; only the probe's accept-any client gets through.
    await using var api = new JsonApiFixture (_ => throw new HttpRequestException (message: "The SSL connection could not be established",
                                                                                   inner: new AuthenticationException ("untrusted")));

    ConnectionTestResult result = await api.TestAsync (JsonApiFixture.Connection ());

    Assert.Equal (expected: ConnectionTestLevel.Warning, actual: result.Level);
    Assert.Contains (expectedSubstring: "doesn't trust its certificate", actualString: result.Message);
  }

  [Fact]
  public async Task Test_WithoutABaseUrl_AsksForOne ()
  {
    await using var api = new JsonApiFixture (JsonApiTests.ServeSample);

    ConnectionTestResult result = await api.TestAsync (connectionJson: "{}");

    Assert.Equal (expected: ConnectionTestLevel.Danger, actual: result.Level);
    Assert.Equal (expected: "Enter the JSON API base URL to test.", actual: result.Message);
  }

  private static HttpResponseMessage ServeSample (HttpRequestMessage request) => JsonApiFixture.Json (TestConnections.HttpbinJson);

  /// <summary>
  ///   Builds a value widget's options.
  /// </summary>
  /// <param name="valuePath">The path to the value.</param>
  /// <param name="path">The path to request.</param>
  /// <param name="extra">More options, as a JSON object.</param>
  /// <returns>The options as stored.</returns>
  private static string Options (string valuePath, string path = "/json", string extra = "{}")
    => JsonSerializer.Serialize (new Dictionary<string, object>
                                                  {
                                                    ["path"]      = path,
                                                    ["valuePath"] = valuePath,
                                                  }
                                                   .Concat (JsonSerializer
                                                                  .Deserialize<Dictionary<string, object>> (extra)!)
                                                   .ToDictionary (p => p.Key, p => p.Value));

  /// <summary>
  ///   Some content that doesn't know its length, as a chunked answer doesn't.
  /// </summary>
  /// <param name="bytes">The content.</param>
  private sealed class UnknownLengthContent (byte[] bytes) : HttpContent
  {
    /// <inheritdoc />
    protected override Task SerializeToStreamAsync (Stream stream, TransportContext? context)
      => stream.WriteAsync (buffer: bytes, offset: 0, count: bytes.Length);

    /// <inheritdoc />
    protected override bool TryComputeLength (out long length)
    {
      length = 0;

      return false;
    }
  }

  /// <summary>
  ///   The services the JSON API widget and connection test resolve, over a stubbed network.
  /// </summary>
  private sealed class JsonApiFixture : IAsyncDisposable
  {
    private readonly MemoryCache     cache = new (new MemoryCacheOptions ());
    private readonly ServiceProvider services;

    /// <summary>
    ///   Initializes a new instance of the <see cref="JsonApiFixture" /> class.
    /// </summary>
    /// <param name="respond">How the stubbed network answers.</param>
    public JsonApiFixture (Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
      this.Handler = new StubHttpMessageHandler (respond);

      var factory = new StubHttpClientFactory (new Dictionary<string, HttpMessageHandler>
                                               {
                                                 [JsonApiClient.ClientName]                = this.Handler,
                                                 [UrlProbe.ClientName]                     = this.Handler,
                                                 [UrlProbe.AcceptAnyCertificateClientName] = new StubHttpMessageHandler (_ => new HttpResponseMessage (HttpStatusCode.OK)),
                                               });

      this.services = new ServiceCollection ().AddSingleton (new JsonApiClient (httpClientFactory: factory, cache: this.cache))
                                              .AddSingleton (new UrlProbe (factory))
                                              .BuildServiceProvider ();
    }

    /// <summary>
    ///   Gets the handler that answers (and records) the requests to the API.
    /// </summary>
    public StubHttpMessageHandler Handler { get; }

    /// <summary>
    ///   Answers with a JSON document.
    /// </summary>
    /// <param name="body">The document.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Json (string body)
      => new (HttpStatusCode.OK) { Content = new StringContent (content: body, encoding: Encoding.UTF8, mediaType: "application/json") };

    /// <summary>
    ///   Builds a connection's stored options.
    /// </summary>
    /// <param name="baseUrl">The base URL.</param>
    /// <param name="auth">The authentication method.</param>
    /// <param name="username">The username.</param>
    /// <param name="headerName">The header's name.</param>
    /// <returns>The options as stored.</returns>
    public static string Connection (string baseUrl    = TestConnections.JsonApiBaseUrl,
                                     string auth       = JsonApiAuth.None,
                                     string username   = "",
                                     string headerName = "")
      => TestConnections.JsonApi (baseUrl: baseUrl, auth: auth, username: username, headerName: headerName).OptionsJson;

    /// <summary>
    ///   Reads a value widget.
    /// </summary>
    /// <param name="optionsJson">The widget's options.</param>
    /// <param name="connectionJson">The connection's stored options.</param>
    /// <param name="credential">The connection's credential.</param>
    /// <returns>What the widget shows.</returns>
    public Task<WidgetResult> FetchAsync (string optionsJson, string? connectionJson = null, string? credential = null)
      => JsonApiTests.Value.FetchAsync (new WidgetContext (Connection: this.Options (json: connectionJson, credential: credential),
                                                           Options: new OptionValues (schema: JsonApiTests.Value.Options, json: optionsJson),
                                                           Services: this.services,
                                                           CancellationToken: TestContext.Current.CancellationToken));

    /// <summary>
    ///   Tests a connection.
    /// </summary>
    /// <param name="connectionJson">The connection's stored options.</param>
    /// <param name="credential">The connection's credential.</param>
    /// <returns>What the test found.</returns>
    public Task<ConnectionTestResult> TestAsync (string? connectionJson = null, string? credential = null)
      => JsonApiTests.Integration.TestAsync (new ConnectionContext (Options: this.Options (json: connectionJson, credential: credential),
                                                                    Services: this.services,
                                                                    CancellationToken: TestContext.Current.CancellationToken));

    /// <inheritdoc />
    public async ValueTask DisposeAsync ()
    {
      await this.services.DisposeAsync ();
      this.cache.Dispose ();
    }

    /// <summary>
    ///   Reads a connection's options.
    /// </summary>
    /// <param name="json">The stored options; a connection to the test API by default.</param>
    /// <param name="credential">The credential.</param>
    /// <returns>The options.</returns>
    private OptionValues Options (string? json, string? credential)
      => new (schema: JsonApiTests.Integration.ConnectionOptions,
              json: json ?? JsonApiFixture.Connection (),
              secrets: credential == null ? null : new Dictionary<string, string> { [JsonApiIntegration.CredentialOption] = credential });
  }
}

using System.Net;
using System.Text.Json;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Tests.Infrastructure;
using h2s.Widgets.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for Settings → Integrations: the cards built from each integration's schema, and their save, test and remove
///   handlers.
/// </summary>
public class IntegrationSettingsTests
{
  private const string SettingsPath = "/Admin/Settings";

  private const string Token = "s3cret-t0ken";

  [Fact]
  public async Task Get_RendersACardPerIntegration ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: IntegrationSettingsTests.SettingsPath,
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """<section id="integration-uptime-kuma" """,                     actualString: html);
    Assert.Contains (expectedSubstring: """data-role="integration-form" data-integration="uptime-kuma""", actualString: html);

    JsonElement card = IntegrationSettingsTests.ReadCards (html).Single (c => c.GetProperty ("key").GetString () == "uptime-kuma");

    Assert.True (card.GetProperty ("isConfigured").GetBoolean ());
    Assert.Equal (expected: TestConnections.KumaServerUrl,
                  actual: card.GetProperty ("values").GetProperty ("serverUrl").GetString ());
    Assert.Equal (expected: ["serverUrl", "apiKey", "defaultUptimeWindow"],
                  actual: card.GetProperty ("options").EnumerateArray ().Select (o => o.GetProperty ("key").GetString ()));
  }

  [Fact]
  public async Task SaveConnection_CreatesTheConnectionThenUpdatesIt ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    JsonElement created = await IntegrationSettingsTests.PostOkAsync (client: client,
                                                                      handler: "SaveConnection",
                                                                      fields: IntegrationSettingsTests
                                                                       .KumaFields (serverUrl: " https://status.home.arpa ",
                                                                                    window: "30d"));

    Assert.True (created.GetProperty ("isConfigured").GetBoolean ());
    Assert.Equal (expected: new UptimeKumaSettings (ServerUrl: TestConnections.KumaServerUrl,
                                                    DefaultUptimeWindow: "30d"),
                  actual: await TestConnections.ReadUptimeKumaAsync (factory));

    _ = await IntegrationSettingsTests.PostOkAsync (client: client,
                                                    handler: "SaveConnection",
                                                    fields:
                                                    IntegrationSettingsTests.KumaFields (serverUrl: "https://kuma.home.arpa",
                                                                                         window: "1d"));

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Connection connection = await context.Connections.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal (expected: new UptimeKumaSettings (ServerUrl: "https://kuma.home.arpa",
                                                      DefaultUptimeWindow: "1d"),
                    actual: await TestConnections.ReadUptimeKumaAsync (context));
      Assert.Equal (expected: "Uptime Kuma", actual: connection.Name);
    }
  }

  [Theory]
  [InlineData ("",                                   "1d", "Server URL is required.")]
  [InlineData ("ftp://status.home.arpa",             "1d", "Server URL must be an HTTP or HTTPS URL.")]
  [InlineData ("https://status.home.arpa/dashboard", "1d", UptimeKumaIntegration.InvalidServerUrl)]
  [InlineData ("https://status.home.arpa",           "7d", "Default uptime period must be one of 1 day, 30 days, 365 days.")]
  [InlineData ("https://status.home.arpa",           "",   "Default uptime period is required.")]
  public async Task SaveConnection_RejectsInvalidValues (string serverUrl, string window, string detail)
  {
    await using var factory = new DashboardApplicationFactory ();

    await FormPoster.AssertProblemAsync (response: await FormPoster.PostAsync (client: factory.CreateClient (),
                                                                               pagePath: IntegrationSettingsTests.SettingsPath,
                                                                               handler: "SaveConnection",
                                                                               fields: IntegrationSettingsTests
                                                                                .KumaFields (serverUrl: serverUrl,
                                                                                             window: window)),
                                         status: HttpStatusCode.BadRequest,
                                         detail: detail);
    Assert.Null (await TestConnections.ReadUptimeKumaAsync (factory));
  }

  [Theory]
  [InlineData ("SaveConnection")]
  [InlineData ("TestConnection")]
  [InlineData ("RemoveConnection")]
  public async Task Handlers_RejectAnUnknownIntegration (string handler)
  {
    await using var factory = new DashboardApplicationFactory ();

    await FormPoster.AssertProblemAsync (response: await FormPoster.PostAsync (client: factory.CreateClient (),
                                                                               pagePath: IntegrationSettingsTests.SettingsPath,
                                                                               handler: handler,
                                                                               fields:
                                                                               [
                                                                                 new KeyValuePair<string,
                                                                                   string> (key: "integration", value: "weather"),
                                                                               ]),
                                         status: HttpStatusCode.NotFound,
                                         detail: "h2s has no such integration.");
  }

  [Fact]
  public async Task RemoveConnection_TurnsTheIntegrationOff ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync ();

    JsonElement card = await IntegrationSettingsTests.PostOkAsync (client: factory.CreateClient (),
                                                                   handler: "RemoveConnection",
                                                                   fields:
                                                                   [
                                                                     new KeyValuePair<string, string> (key: "integration",
                                                                                                       value: "uptime-kuma"),
                                                                   ]);

    Assert.False (card.GetProperty ("isConfigured").GetBoolean ());
    Assert.Equal (expected: JsonValueKind.Null, actual: card.GetProperty ("values").GetProperty ("serverUrl").ValueKind);
    Assert.Equal (expected: "1d", actual: card.GetProperty ("values").GetProperty ("defaultUptimeWindow").GetString ());
    Assert.Null (await TestConnections.ReadUptimeKumaAsync (factory));
  }

  [Fact]
  public async Task TestConnection_ReadsTheMetrics_WithoutSaving ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };

    JsonElement result = await IntegrationSettingsTests.PostOkAsync (client: factory.CreateClient (),
                                                                     handler: "TestConnection",
                                                                     fields: IntegrationSettingsTests
                                                                      .KumaFields (serverUrl: "https://status.home.arpa",
                                                                                   window: "1d"));

    Assert.Equal (expected: "success", actual: result.GetProperty ("level").GetString ());
    Assert.Equal (expected: "Connected. Uptime Kuma reports 2 monitors and 1 group.",
                  actual: result.GetProperty ("message").GetString ());
    Assert.Null (await TestConnections.ReadUptimeKumaAsync (factory));
  }

  [Fact]
  public async Task TestConnection_SaysWhenTheApiKeyIsRefused ()
  {
    await using var factory = new DashboardApplicationFactory { Outbound = TestConnections.ServeKuma };

    JsonElement result = await IntegrationSettingsTests.PostOkAsync (client: factory.CreateClient (),
                                                                     handler: "TestConnection",
                                                                     fields: IntegrationSettingsTests
                                                                      .KumaFields (serverUrl: "https://status.home.arpa",
                                                                                   window: "1d",
                                                                                   apiKey: "wrong"));

    Assert.Equal (expected: "danger", actual: result.GetProperty ("level").GetString ());
    Assert.Equal (expected: "Connected to Uptime Kuma, but: Uptime Kuma refused the API key.",
                  actual: result.GetProperty ("message").GetString ());
  }

  [Fact]
  public async Task SaveConnection_RequiresAnApiKey ()
  {
    await using var factory = new DashboardApplicationFactory ();

    await FormPoster.AssertProblemAsync (response: await FormPoster.PostAsync (client: factory.CreateClient (),
                                                                               pagePath: IntegrationSettingsTests.SettingsPath,
                                                                               handler: "SaveConnection",
                                                                               fields: IntegrationSettingsTests
                                                                                .KumaFields (serverUrl: "https://status.home.arpa",
                                                                                             window: "1d",
                                                                                             apiKey: null)),
                                         status: HttpStatusCode.BadRequest,
                                         detail: "API key is required.");
  }

  [Fact]
  public async Task TestConnection_ReportsAnUnreachableServer ()
  {
    await using var factory = new DashboardApplicationFactory ();
    factory.Outbound = _ => throw new HttpRequestException ("Connection refused");

    JsonElement result = await IntegrationSettingsTests.PostOkAsync (client: factory.CreateClient (),
                                                                     handler: "TestConnection",
                                                                     fields: IntegrationSettingsTests
                                                                      .KumaFields (serverUrl: "https://status.home.arpa",
                                                                                   window: "1d"));

    Assert.Equal (expected: "danger", actual: result.GetProperty ("level").GetString ());
    Assert.Equal (expected: "Couldn't reach Uptime Kuma at https://status.home.arpa.",
                  actual: result.GetProperty ("message").GetString ());
  }

  [Fact]
  public async Task Secrets_AreStoredEncrypted_AndNeverSentBack ()
  {
    await using DashboardApplicationFactory factory = IntegrationSettingsTests.CreateFactoryWithTokenIntegration ();
    HttpClient                              client  = factory.CreateClient ();

    JsonElement card = await IntegrationSettingsTests.PostOkAsync (client: client,
                                                                   handler: "SaveConnection",
                                                                   fields: IntegrationSettingsTests
                                                                    .TokenFields (token: IntegrationSettingsTests.Token));

    Assert.True (card.GetProperty ("secretsSet").GetProperty ("token").GetBoolean ());
    Assert.DoesNotContain (expectedSubstring: IntegrationSettingsTests.Token, actualString: card.GetRawText ());

    string html = await client.GetStringAsync (requestUri: IntegrationSettingsTests.SettingsPath,
                                               cancellationToken: TestContext.Current.CancellationToken);

    Assert.DoesNotContain (expectedSubstring: IntegrationSettingsTests.Token, actualString: html);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Connection connection = await context.Connections.SingleAsync (TestContext.Current.CancellationToken);

      Assert.DoesNotContain (expectedSubstring: IntegrationSettingsTests.Token, actualString: connection.OptionsJson);
      Assert.DoesNotContain (expectedSubstring: IntegrationSettingsTests.Token, actualString: connection.SecretsJson!);
    }
  }

  [Fact]
  public async Task Secrets_LeftBlankAreKept_ForSavingAndTesting_UntilCleared ()
  {
    await using DashboardApplicationFactory factory = IntegrationSettingsTests.CreateFactoryWithTokenIntegration ();
    HttpClient                              client  = factory.CreateClient ();

    _ = await IntegrationSettingsTests.PostOkAsync (client: client,
                                                    handler: "SaveConnection",
                                                    fields: IntegrationSettingsTests.TokenFields (token: IntegrationSettingsTests
                                                                                                   .Token));

    // Saved again without the token: it's kept.
    JsonElement kept = await IntegrationSettingsTests.PostOkAsync (client: client,
                                                                   handler: "SaveConnection",
                                                                   fields: IntegrationSettingsTests.TokenFields ());

    Assert.True (kept.GetProperty ("secretsSet").GetProperty ("token").GetBoolean ());

    JsonElement tested = await IntegrationSettingsTests.PostOkAsync (client: client,
                                                                     handler: "TestConnection",
                                                                     fields: IntegrationSettingsTests.TokenFields ());

    Assert.Equal (expected: "success", actual: tested.GetProperty ("level").GetString ());

    // Testing with the token cleared leaves it out, without clearing what's stored.
    JsonElement testedWithout = await IntegrationSettingsTests.PostOkAsync (client: client,
                                                                            handler: "TestConnection",
                                                                            fields: IntegrationSettingsTests
                                                                             .TokenFields (clear: true));

    Assert.Equal (expected: "warning", actual: testedWithout.GetProperty ("level").GetString ());

    JsonElement cleared = await IntegrationSettingsTests.PostOkAsync (client: client,
                                                                      handler: "SaveConnection",
                                                                      fields: IntegrationSettingsTests.TokenFields (clear: true));

    Assert.False (cleared.GetProperty ("secretsSet").GetProperty ("token").GetBoolean ());
  }

  /// <summary>
  ///   Creates an application with <see cref="TokenIntegration" /> registered beside the built-in integrations.
  /// </summary>
  /// <returns>The application factory.</returns>
  private static DashboardApplicationFactory CreateFactoryWithTokenIntegration ()
    => new () { ConfigureServices = services => services.AddSingleton<IIntegration, TokenIntegration> () };

  /// <summary>
  ///   Builds the fields the Uptime Kuma card posts.
  /// </summary>
  /// <param name="serverUrl">The server URL.</param>
  /// <param name="window">The default uptime window.</param>
  /// <param name="apiKey">A newly typed API key, if any.</param>
  /// <returns>The fields.</returns>
  private static KeyValuePair<string, string>[] KumaFields (string  serverUrl,
                                                            string  window,
                                                            string? apiKey = TestConnections.KumaApiKey)
    =>
    [
      new (key: "integration", value: UptimeKumaIntegration.IntegrationKey),
      new (key: "options[serverUrl]", value: serverUrl),
      new (key: "options[defaultUptimeWindow]", value: window),
      .. apiKey == null ? [] : new[] { new KeyValuePair<string, string> (key: "secrets[apiKey]", value: apiKey) },
    ];

  /// <summary>
  ///   Builds the fields the token integration's card posts.
  /// </summary>
  /// <param name="token">A newly typed token, if any.</param>
  /// <param name="clear">Whether to clear the stored token.</param>
  /// <returns>The fields.</returns>
  private static KeyValuePair<string, string>[] TokenFields (string? token = null, bool clear = false)
    =>
    [
      new (key: "integration", value: TokenIntegration.IntegrationKey),
      new (key: "options[server]", value: "https://app.home.arpa"),
      .. token == null ? [] : new[] { new KeyValuePair<string, string> (key: "secrets[token]", value: token) },
      .. clear ? new[] { new KeyValuePair<string, string> (key: "clearSecrets",                value: "token") } : [],
    ];

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
                                                               pagePath: IntegrationSettingsTests.SettingsPath,
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

  /// <summary>
  ///   A test-only integration with a server and a secret token. Its test reports whether it was given the token.
  /// </summary>
  private sealed class TokenIntegration : IIntegration
  {
    /// <summary>
    ///   The integration's key.
    /// </summary>
    public const string IntegrationKey = "token-test";

    /// <inheritdoc />
    public string Key => TokenIntegration.IntegrationKey;

    /// <inheritdoc />
    public string DisplayName => "Token test";

    /// <inheritdoc />
    public string Description => "An integration with a secret token, for tests.";

    /// <inheritdoc />
    public bool AllowsMultipleConnections => false;

    /// <inheritdoc />
    public IReadOnlyList<SchemaOption> ConnectionOptions { get; } =
      [
        new (Key: "server", Label: "Server", Type: OptionType.Url), new (Key: "token", Label: "Token", Type: OptionType.Secret),
      ];

    /// <inheritdoc />
    public Task<ConnectionTestResult> TestAsync (ConnectionContext context)
      => Task.FromResult (context.Options.GetSecret ("token") == IntegrationSettingsTests.Token
                            ? new ConnectionTestResult (Level: ConnectionTestLevel.Success, Message: "The token was given.")
                            : new ConnectionTestResult (Level: ConnectionTestLevel.Warning, Message: "No token."));
  }
}

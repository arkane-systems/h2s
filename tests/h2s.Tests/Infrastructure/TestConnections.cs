using System.Net;
using System.Text;
using System.Text.Json;
using h2s.Data;
using h2s.Integrations;
using h2s.Integrations.JsonApi;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Widgets;
using h2s.Widgets.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace h2s.Tests.Infrastructure;

/// <summary>
///   Arranges and reads connections in a test database.
/// </summary>
public static class TestConnections
{
  /// <summary>
  ///   The Uptime Kuma server the tests' connections point at.
  /// </summary>
  public const string KumaServerUrl = "https://status.home.arpa";

  /// <summary>
  ///   The API key the stubbed Uptime Kuma accepts.
  /// </summary>
  public const string KumaApiKey = "uk1_test-key";

  /// <summary>
  ///   A trimmed <c>/metrics</c> answer in Uptime Kuma 2.x's shape: a group monitor and two others, one with a certificate.
  /// </summary>
  public const string KumaMetricsText = """
                                        # HELP monitor_status Monitor Status (1 = UP, 0= DOWN, 2= PENDING, 3= MAINTENANCE)
                                        # TYPE monitor_status gauge
                                        monitor_status{monitor_id="1",monitor_name="Infrastructure",monitor_type="group",monitor_url="https://",monitor_hostname="null",monitor_port="null"} 1
                                        monitor_status{media="",monitor_id="11",monitor_name="Jellyfin",monitor_type="http",monitor_url="https://jellyfin.home.arpa/",monitor_hostname="null",monitor_port="null"} 0
                                        monitor_status{monitor_id="12",monitor_name="Sonarr \"TV\"",monitor_type="http",monitor_url="https://sonarr.home.arpa/",monitor_hostname="null",monitor_port="null"} 1
                                        # HELP monitor_response_time Monitor Response Time (ms)
                                        monitor_response_time{media="",monitor_id="11",monitor_name="Jellyfin",monitor_type="http",monitor_url="https://jellyfin.home.arpa/",monitor_hostname="null",monitor_port="null"} 42
                                        monitor_uptime_ratio{monitor_id="1",monitor_name="Infrastructure",monitor_type="group",monitor_url="https://",monitor_hostname="null",monitor_port="null",window="1d"} 0.99861
                                        monitor_uptime_ratio{monitor_id="1",monitor_name="Infrastructure",monitor_type="group",monitor_url="https://",monitor_hostname="null",monitor_port="null",window="30d"} 0.9998
                                        monitor_cert_days_remaining{media="",monitor_id="11",monitor_name="Jellyfin",monitor_type="http",monitor_url="https://jellyfin.home.arpa/",monitor_hostname="null",monitor_port="null"} 439
                                        monitor_cert_is_valid{media="",monitor_id="11",monitor_name="Jellyfin",monitor_type="http",monitor_url="https://jellyfin.home.arpa/",monitor_hostname="null",monitor_port="null"} 1
                                        """;

  /// <summary>
  ///   Answers like an Uptime Kuma server: <see cref="KumaMetricsText" /> at <c>/metrics</c> for
  ///   <see cref="KumaApiKey" /> (401 for anything else), OK at its root, and 404 elsewhere.
  /// </summary>
  /// <param name="request">The outbound request.</param>
  /// <returns>The response.</returns>
  public static HttpResponseMessage ServeKuma (HttpRequestMessage request)
    => request.RequestUri!.AbsolutePath switch
       {
         "/" => new HttpResponseMessage (HttpStatusCode.OK),
         "/metrics" when request.Headers.Authorization is { Scheme: "Basic", Parameter: { } credentials } &&
                         (Encoding.UTF8.GetString (Convert.FromBase64String (credentials)) == $":{TestConnections.KumaApiKey}")
           => new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (TestConnections.KumaMetricsText) },
         "/metrics" => new HttpResponseMessage (HttpStatusCode.Unauthorized),
         _          => new HttpResponseMessage (HttpStatusCode.NotFound),
       };

  /// <summary>
  ///   The JSON API the tests' connections point at.
  /// </summary>
  public const string JsonApiBaseUrl = "https://api.home.arpa";

  /// <summary>
  ///   The bearer token the stubbed JSON API accepts at its root.
  /// </summary>
  public const string JsonApiToken = "api-test-token";

  /// <summary>
  ///   httpbin's <c>/json</c> sample, which the stubbed JSON API serves at <c>/json</c>.
  /// </summary>
  public const string HttpbinJson = """
                                    {
                                      "slideshow": {
                                        "author": "Yours Truly",
                                        "date": "date of publication",
                                        "slides": [
                                          {
                                            "title": "Wake up to WonderWidgets!",
                                            "type": "all"
                                          },
                                          {
                                            "items": [
                                              "Why <em>WonderWidgets</em> are great",
                                              "Who <em>buys</em> WonderWidgets"
                                            ],
                                            "title": "Overview",
                                            "type": "all"
                                          }
                                        ],
                                        "title": "Sample Slide Show"
                                      }
                                    }
                                    """;

  /// <summary>
  ///   Answers like a JSON API: <see cref="HttpbinJson" /> at <c>/json</c>, and at its root OK for a request bearing
  ///   <see cref="JsonApiToken" /> and 401 for any other.
  /// </summary>
  /// <param name="request">The outbound request.</param>
  /// <returns>The response.</returns>
  public static HttpResponseMessage ServeJsonApi (HttpRequestMessage request)
    => request.RequestUri!.AbsolutePath switch
       {
         "/json" => new HttpResponseMessage (HttpStatusCode.OK)
                    {
                      Content = new StringContent (content: TestConnections.HttpbinJson,
                                                   encoding: Encoding.UTF8,
                                                   mediaType: "application/json"),
                    },
         "/" when request.Headers.Authorization is { Scheme: "Bearer", Parameter: TestConnections.JsonApiToken }
           => new HttpResponseMessage (HttpStatusCode.OK),
         "/" => new HttpResponseMessage (HttpStatusCode.Unauthorized),
         _   => new HttpResponseMessage (HttpStatusCode.NotFound),
       };

  /// <summary>
  ///   Creates a JSON API connection, as Settings saves it.
  /// </summary>
  /// <param name="baseUrl">The base URL.</param>
  /// <param name="auth">The authentication method.</param>
  /// <param name="username">The username, for basic authentication.</param>
  /// <param name="headerName">The header's name, for custom-header authentication.</param>
  /// <returns>The connection, not yet added to a context, without a credential.</returns>
  public static Connection JsonApi (string baseUrl    = TestConnections.JsonApiBaseUrl,
                                    string auth       = JsonApiAuth.None,
                                    string username   = "",
                                    string headerName = "")
    => new ()
       {
         IntegrationKey = JsonApiIntegration.IntegrationKey,
         Name           = "JSON API",
         OptionsJson = JsonSerializer.Serialize (new Dictionary<string, object>
                                                 {
                                                   [JsonApiIntegration.BaseUrlOption]    = baseUrl,
                                                   [JsonApiIntegration.AuthOption]       = auth,
                                                   [JsonApiIntegration.UsernameOption]   = username,
                                                   [JsonApiIntegration.HeaderNameOption] = headerName,
                                                 }),
       };

  /// <summary>
  ///   Adds a JSON API connection to an application's database. Call it before the first request, since the app caches
  ///   connections.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="baseUrl">The base URL.</param>
  /// <param name="auth">The authentication method.</param>
  /// <param name="credential">The credential, encrypted with the app's own Data Protection keys; <c>null</c> for none.</param>
  public static async Task AddJsonApiAsync (this DashboardApplicationFactory factory,
                                            string                           baseUrl    = TestConnections.JsonApiBaseUrl,
                                            string                           auth       = JsonApiAuth.None,
                                            string?                          credential = null)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Connection connection = TestConnections.JsonApi (baseUrl: baseUrl, auth: auth);

      if (credential != null)
      {
        connection.SecretsJson = scope.ServiceProvider.GetRequiredService<ConnectionSecrets> ()
                                      .Protect (new Dictionary<string, string> { [JsonApiIntegration.CredentialOption] = credential });
      }

      context.Connections.Add (connection);
      await context.SaveChangesAsync (TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Creates an Uptime Kuma connection, as Settings saves it.
  /// </summary>
  /// <param name="defaultUptimeWindow">The default uptime window.</param>
  /// <param name="serverUrl">The server URL.</param>
  /// <returns>The connection, not yet added to a context, without an API key.</returns>
  public static Connection UptimeKuma (string defaultUptimeWindow = UptimeKumaWindows.OneDay,
                                       string serverUrl           = TestConnections.KumaServerUrl)
    => new ()
       {
         IntegrationKey = UptimeKumaIntegration.IntegrationKey,
         Name           = "Uptime Kuma",
         OptionsJson = JsonSerializer.Serialize (new Dictionary<string, object>
                                                 {
                                                   [UptimeKumaIntegration.ServerUrlOption]           = serverUrl,
                                                   [UptimeKumaIntegration.DefaultUptimeWindowOption] = defaultUptimeWindow,
                                                 }),
       };

  /// <summary>
  ///   Creates a connection service with the built-in integrations, for unit tests that build their services by hand.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <param name="cache">The cache the service keeps the connections in.</param>
  /// <returns>The service.</returns>
  public static ConnectionService CreateService (DashboardContext context, IMemoryCache cache)
    => new (context: context,
            integrations: new IntegrationCatalog ([new UptimeKumaIntegration (), new JsonApiIntegration ()]),
            secrets: new ConnectionSecrets (new EphemeralDataProtectionProvider ()),
            cache: cache);

  /// <summary>
  ///   Creates a widget service with the built-in integrations' kinds, for unit tests that build their services by hand.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <param name="connections">The connection service.</param>
  /// <param name="cache">The cache the widget results are kept in.</param>
  /// <param name="services">The services the kinds resolve what they need from.</param>
  /// <returns>The service.</returns>
  public static WidgetService CreateWidgetService (DashboardContext  context,
                                                   ConnectionService connections,
                                                   IMemoryCache      cache,
                                                   IServiceProvider  services)
    => new (context: context,
            connections: connections,
            catalog: new WidgetCatalog (new IntegrationCatalog ([new UptimeKumaIntegration (), new JsonApiIntegration ()])),
            fetcher: new WidgetFetcher (cache: cache, logger: NullLogger<WidgetFetcher>.Instance),
            services: services);

  /// <summary>
  ///   Adds an Uptime Kuma connection to an application's database. Call it before the first request, since the app caches
  ///   connections.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <param name="defaultUptimeWindow">The default uptime window.</param>
  /// <param name="apiKey">The API key, encrypted with the app's own Data Protection keys; <c>null</c> for none.</param>
  public static async Task AddUptimeKumaAsync (this DashboardApplicationFactory factory,
                                               string                           defaultUptimeWindow = UptimeKumaWindows.OneDay,
                                               string?                          apiKey              = null)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Connection kuma = TestConnections.UptimeKuma (defaultUptimeWindow: defaultUptimeWindow);

      if (apiKey != null)
      {
        kuma.SecretsJson = scope.ServiceProvider.GetRequiredService<ConnectionSecrets> ()
                                .Protect (new Dictionary<string, string> { [UptimeKumaIntegration.ApiKeyOption] = apiKey });
      }

      context.Connections.Add (kuma);
      await context.SaveChangesAsync (TestContext.Current.CancellationToken);
    }
  }

  /// <summary>
  ///   Reads the Uptime Kuma connection's settings from a database.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <returns>The settings, or <c>null</c> when there's no Uptime Kuma connection.</returns>
  public static async Task<UptimeKumaSettings?> ReadUptimeKumaAsync (DashboardContext context)
  {
    Connection? connection = await context.Connections
                                          .AsNoTracking ()
                                          .SingleOrDefaultAsync (predicate: c => c.IntegrationKey ==
                                                                                 UptimeKumaIntegration.IntegrationKey,
                                                                 cancellationToken: TestContext.Current.CancellationToken);

    return connection == null
             ? null
             : UptimeKumaSettings.From (new OptionValues (schema: new UptimeKumaIntegration ().ConnectionOptions,
                                                          json: connection.OptionsJson));
  }

  /// <summary>
  ///   Reads the Uptime Kuma connection's settings from an application's database.
  /// </summary>
  /// <param name="factory">The application factory.</param>
  /// <returns>The settings, or <c>null</c> when there's no Uptime Kuma connection.</returns>
  public static async Task<UptimeKumaSettings?> ReadUptimeKumaAsync (DashboardApplicationFactory factory)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await TestConnections.ReadUptimeKumaAsync (context);
    }
  }
}

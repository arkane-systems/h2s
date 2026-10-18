using System.Net;
using h2s.Services;
using h2s.Widgets.Abstractions;

namespace h2s.Integrations.JsonApi;

/// <summary>
///   The JSON API integration: any service that answers HTTP requests with JSON, whose values the dashboard shows as
///   widgets on links and in status bars. A connection names the service's base URL and how requests authenticate; each
///   widget names a path under it and a value in the answer.
/// </summary>
public sealed class JsonApiIntegration : IIntegration
{
  /// <summary>
  ///   The integration's key.
  /// </summary>
  public const string IntegrationKey = "json-api";

  /// <summary>
  ///   The key of the option holding the base URL.
  /// </summary>
  public const string BaseUrlOption = "baseUrl";

  /// <summary>
  ///   The key of the option holding the authentication method (<see cref="JsonApiAuth" />).
  /// </summary>
  public const string AuthOption = "auth";

  /// <summary>
  ///   The key of the option holding the username, for basic authentication.
  /// </summary>
  public const string UsernameOption = "username";

  /// <summary>
  ///   The key of the option holding the header's name, for custom-header authentication.
  /// </summary>
  public const string HeaderNameOption = "headerName";

  /// <summary>
  ///   The key of the secret option holding the password, bearer token or header value.
  /// </summary>
  public const string CredentialOption = "credential";

  /// <summary>
  ///   The message for a base URL that isn't usable.
  /// </summary>
  public const string InvalidBaseUrl =
    "JSON API base URL must be a valid HTTP or HTTPS URL, with an optional path such as /api but no query or fragment.";

  /// <summary>
  ///   The message for basic authentication without a username.
  /// </summary>
  public const string UsernameRequired = "Basic authentication needs a username.";

  /// <summary>
  ///   The message for custom-header authentication without a header name.
  /// </summary>
  public const string HeaderNameRequired = "Custom header authentication needs a header name.";

  /// <summary>
  ///   Builds a test result.
  /// </summary>
  /// <param name="level">How the test went.</param>
  /// <param name="message">What it found.</param>
  /// <returns>The result.</returns>
  private static ConnectionTestResult Result (ConnectionTestLevel level, string message) => new (Level: level, Message: message);

  /// <summary>
  ///   Says why a service couldn't be reached, telling an untrusted certificate from an unreachable server.
  /// </summary>
  /// <param name="settings">The connection's settings.</param>
  /// <param name="context">The connection context.</param>
  /// <returns>The test's result.</returns>
  private static async Task<ConnectionTestResult> ExplainUnreachableAsync (JsonApiSettings settings, ConnectionContext context)
  {
    switch (await context.Services.GetRequiredService<UrlProbe> ()
                         .CheckAsync (url: settings.BaseUrl, cancellationToken: context.CancellationToken))
    {
      case UrlReachability.Unreachable:
        return JsonApiIntegration.Result (level: ConnectionTestLevel.Danger, message: $"Couldn't reach {settings.BaseUrl}.");

      case UrlReachability.UntrustedCertificate:
        return JsonApiIntegration.Result (level: ConnectionTestLevel.Warning,
                                          message:
                                          "The service answers, but the h2s server doesn't trust its certificate, so it can't read " +
                                          "its values until the CA is mounted (see the README's section on trusting a homelab " +
                                          "certificate authority).");

      default:
        return JsonApiIntegration.Result (level: ConnectionTestLevel.Warning,
                                          message: $"{settings.BaseUrl} answers, but not to a request for JSON in time.");
    }
  }

  /// <inheritdoc />
  public string Key => JsonApiIntegration.IntegrationKey;

  /// <inheritdoc />
  public string DisplayName => "JSON API";

  /// <inheritdoc />
  public string Description
    => "Value widgets for links' tiles and status bars, read from any service that answers with JSON: pick a value out of " +
       "the response, such as a status or a count, and show it as a coloured pill.";

  /// <inheritdoc />
  public bool AllowsMultipleConnections => false;

  /// <inheritdoc />
  public IReadOnlyList<SchemaOption> ConnectionOptions { get; } =
    [
      new (Key: JsonApiIntegration.BaseUrlOption, Label: "Base URL", Type: OptionType.Url)
      {
        IsRequired  = true,
        Placeholder = "https://host.example.com/api",
        HelpText = "Where the service lives, with a path prefix if it has one. Each widget names a path under it. Requests " +
                   "never leave this host, and redirects aren't followed.",
      },
      new (Key: JsonApiIntegration.AuthOption, Label: "Authentication", Type: OptionType.Select)
      {
        IsRequired   = true,
        Choices      = JsonApiAuth.Choices,
        DefaultValue = JsonApiAuth.None,
        HelpText     = "How requests prove who they are. The credential below is the password, token or header value.",
      },
      new (Key: JsonApiIntegration.UsernameOption, Label: "Username", Type: OptionType.Text)
      {
        HelpText = "For basic authentication.",
      },
      new (Key: JsonApiIntegration.HeaderNameOption, Label: "Header name", Type: OptionType.Text)
      {
        Placeholder        = "X-Api-Key",
        HelpText           = "For custom-header authentication: the header that carries the credential.",
        Pattern            = @"[A-Za-z0-9!#$%&'*+.^_`\|~\-]+",
        PatternDescription = "may only contain letters, numbers and the punctuation HTTP allows in a header name",
      },
      new (Key: JsonApiIntegration.CredentialOption, Label: "Credential", Type: OptionType.Secret)
      {
        HelpText = "The password, bearer token or header value. It's stored encrypted and never shown again.",
      },
    ];

  /// <inheritdoc />
  public IReadOnlyList<IWidgetKind> WidgetKinds { get; } = [new JsonApiValueWidgetKind ()];

  /// <inheritdoc />
  /// <remarks>
  ///   The credential isn't checked here: it can't be exported, so an imported connection legitimately lacks it, and a
  ///   missing one is reported by the connection test and by the widgets themselves.
  /// </remarks>
  public string? Validate (OptionValues options)
  {
    if (!JsonApiUrls.IsValidBaseUrl (options.GetText (JsonApiIntegration.BaseUrlOption).Trim ()))
    {
      return JsonApiIntegration.InvalidBaseUrl;
    }

    return options.GetText (JsonApiIntegration.AuthOption) switch
           {
             JsonApiAuth.Basic when options.GetText (JsonApiIntegration.UsernameOption).Trim ().Length == 0
               => JsonApiIntegration.UsernameRequired,
             JsonApiAuth.Header when options.GetText (JsonApiIntegration.HeaderNameOption).Trim ().Length == 0
               => JsonApiIntegration.HeaderNameRequired,
             _ => null,
           };
  }

  /// <inheritdoc />
  /// <remarks>
  ///   It requests the base URL with the connection's authentication. Widgets read paths under the base, so a 404 there is
  ///   only a warning; a refused credential is a failure.
  /// </remarks>
  public async Task<ConnectionTestResult> TestAsync (ConnectionContext context)
  {
    if (JsonApiSettings.From (context.Options) is not { } settings)
    {
      return JsonApiIntegration.Result (level: ConnectionTestLevel.Danger, message: "Enter the JSON API base URL to test.");
    }

    if (settings.AuthProblem is { } problem)
    {
      return JsonApiIntegration.Result (level: ConnectionTestLevel.Danger, message: problem);
    }

    JsonApiResponse response = await context.Services.GetRequiredService<JsonApiClient> ()
                                            .GetAsync (settings: settings,
                                                       url: new Uri (settings.BaseUrl),
                                                       useCache: false,
                                                       cancellationToken: context.CancellationToken);

    if (response.IsUnreachable)
    {
      return await JsonApiIntegration.ExplainUnreachableAsync (settings: settings, context: context);
    }

    if (response.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices)
    {
      return JsonApiIntegration.Result (level: ConnectionTestLevel.Success,
                                        message: $"Connected. {settings.BaseUrl} answered {response.Status}.");
    }

    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
    {
      return JsonApiIntegration.Result (level: ConnectionTestLevel.Danger,
                                        message: settings.Auth == JsonApiAuth.None
                                                   ? $"{settings.BaseUrl} answered {response.Status}: it asks for credentials, so " +
                                                     "choose an authentication method."
                                                   : $"{settings.BaseUrl} refused the credentials ({response.Status}).");
    }

    return JsonApiIntegration.Result (level: ConnectionTestLevel.Warning,
                                      message: $"{settings.BaseUrl} answered {response.Status}. Widgets read paths under the " +
                                               "base URL, so that may be fine if nothing is served at the base itself.");
  }
}

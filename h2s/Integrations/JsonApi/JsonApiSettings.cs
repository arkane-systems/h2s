using h2s.Widgets.Abstractions;

namespace h2s.Integrations.JsonApi;

/// <summary>
///   A JSON API connection's settings, as its widgets and connection test read them. A connection always has a base URL.
/// </summary>
/// <param name="BaseUrl">The base URL, which may include a path prefix such as <c>/api</c>.</param>
/// <param name="Auth">How requests authenticate (<see cref="JsonApiAuth" />).</param>
/// <param name="Username">The username, for basic authentication.</param>
/// <param name="HeaderName">The header's name, for custom-header authentication.</param>
public sealed record JsonApiSettings (string BaseUrl, string Auth, string Username, string HeaderName)
{
  /// <summary>
  ///   Gets the credential (the password, bearer token or header value), or <c>null</c> when none is set (or it can no
  ///   longer be decrypted). It's left out of <see cref="ToString" />, so it never reaches a log.
  /// </summary>
  public string? Credential { get; init; }

  /// <summary>
  ///   Gets what's missing from the authentication, as a sentence, or <c>null</c> when the settings are enough to send
  ///   requests. A connection can lack its credential after an import, since secrets are never exported.
  /// </summary>
  public string? AuthProblem
    => this.Auth switch
       {
         JsonApiAuth.None => null,
         JsonApiAuth.Basic when string.IsNullOrEmpty (this.Username) =>
           "The JSON API connection uses basic authentication but has no username.",
         JsonApiAuth.Header when string.IsNullOrEmpty (this.HeaderName) =>
           "The JSON API connection uses a custom header but has no header name.",
         _ when string.IsNullOrEmpty (this.Credential) =>
           "The JSON API connection needs a credential; add one in Settings → Integrations.",
         _ => null,
       };

  /// <summary>
  ///   Reads the settings from a connection's option values.
  /// </summary>
  /// <param name="options">The option values, read against <see cref="JsonApiIntegration" />'s schema.</param>
  /// <returns>The settings, or <c>null</c> when they have no usable base URL.</returns>
  public static JsonApiSettings? From (OptionValues? options)
  {
    string baseUrl = options?.GetText (JsonApiIntegration.BaseUrlOption).Trim () ?? "";

    if (baseUrl.Length == 0)
    {
      return null;
    }

    string auth = options!.GetText (JsonApiIntegration.AuthOption);

    return new JsonApiSettings (BaseUrl: baseUrl,
                                Auth: auth.Length == 0 ? JsonApiAuth.None : auth,
                                Username: options.GetText (JsonApiIntegration.UsernameOption).Trim (),
                                HeaderName: options.GetText (JsonApiIntegration.HeaderNameOption).Trim ())
           {
             Credential = options.GetSecret (JsonApiIntegration.CredentialOption)?.Trim () is { Length: > 0 } credential
                            ? credential
                            : null,
           };
  }

  /// <inheritdoc />
  public override string ToString () => $"JsonApiSettings {{ BaseUrl = {this.BaseUrl}, Auth = {this.Auth} }}";
}

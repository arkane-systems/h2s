using h2s.Widgets.Abstractions;

namespace h2s.Integrations.JsonApi;

/// <summary>
///   The ways a JSON API connection can authenticate.
/// </summary>
public static class JsonApiAuth
{
  /// <summary>
  ///   No authentication.
  /// </summary>
  public const string None = "none";

  /// <summary>
  ///   HTTP basic authentication: the username and the credential as the password.
  /// </summary>
  public const string Basic = "basic";

  /// <summary>
  ///   A bearer token: the credential.
  /// </summary>
  public const string Bearer = "bearer";

  /// <summary>
  ///   A custom header, named by the connection, whose value is the credential.
  /// </summary>
  public const string Header = "header";

  /// <summary>
  ///   Gets the methods as choices for the connection's <c>auth</c> option.
  /// </summary>
  public static IReadOnlyList<OptionChoice> Choices { get; } =
    [
      new (Value: JsonApiAuth.None, Label: "None"),
      new (Value: JsonApiAuth.Basic, Label: "Basic"),
      new (Value: JsonApiAuth.Bearer, Label: "Bearer token"),
      new (Value: JsonApiAuth.Header, Label: "Custom header"),
    ];
}

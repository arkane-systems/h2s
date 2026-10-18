using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using h2s.Integrations.JsonApi;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Services;

/// <summary>
///   Reads a JSON API: one GET, with the connection's authentication, of an answer small enough to hold in memory.
/// </summary>
/// <remarks>
///   Successful answers are cached briefly, so several widgets on one endpoint cost the service one request. Certificates
///   are always validated, and redirects aren't followed, so a connection's credentials only go to the host it names.
/// </remarks>
public class JsonApiClient
{
  /// <summary>
  ///   The name of the HTTP client used to call JSON APIs.
  /// </summary>
  public const string ClientName = "JsonApi";

  /// <summary>
  ///   The most of an answer the client reads, in bytes. A larger answer is reported as too large.
  /// </summary>
  public const int MaxBodyBytes = 256 * 1024;

  /// <summary>
  ///   The time allowed for each request to a JSON API.
  /// </summary>
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds (10);

  // Short, so widgets stay current, but long enough that several on one endpoint share a request.
  private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds (15);

  private readonly IMemoryCache cache;

  private readonly IHttpClientFactory httpClientFactory;

  /// <summary>
  ///   Initializes a new instance of the <see cref="JsonApiClient" /> class.
  /// </summary>
  /// <param name="httpClientFactory">The factory that provides the JSON API client.</param>
  /// <param name="cache">The cache that holds recent answers.</param>
  public JsonApiClient (IHttpClientFactory httpClientFactory, IMemoryCache cache)
  {
    this.httpClientFactory = httpClientFactory;
    this.cache             = cache;
  }

  /// <summary>
  ///   Gets a URL with a connection's authentication.
  /// </summary>
  /// <param name="settings">The connection's settings.</param>
  /// <param name="url">The URL, which the caller has checked lies under the connection's base URL.</param>
  /// <param name="useCache">Whether a recent successful answer may be reused.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>The answer.</returns>
  public async Task<JsonApiResponse> GetAsync (JsonApiSettings   settings,
                                               Uri               url,
                                               bool              useCache          = true,
                                               CancellationToken cancellationToken = default)
  {
    // Neither the credential nor anything else about the authentication goes into the cache's keys, only a hash of it.
    string authentication =
      string.Join (separator: '\0', settings.Auth, settings.Username, settings.HeaderName, settings.Credential);
    var key = $"json-api:{url.AbsoluteUri}:{Convert.ToHexString (SHA256.HashData (Encoding.UTF8.GetBytes (authentication)))}";

    if (useCache && this.cache.TryGetValue (key: key, value: out JsonApiResponse? cached) && (cached != null))
    {
      return cached;
    }

    JsonApiResponse response = await this.SendAsync (settings: settings, url: url, cancellationToken: cancellationToken);

    // Only successful answers are cached, so fixing the service or the credential takes effect straight away.
    if (response.IsSuccess)
    {
      _ = this.cache.Set (key: key, value: response, absoluteExpirationRelativeToNow: JsonApiClient.CacheDuration);
    }

    return response;
  }

  /// <summary>
  ///   Sends the request and reads the answer, as far as it's within the size limit.
  /// </summary>
  /// <param name="settings">The connection's settings.</param>
  /// <param name="url">The URL.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>The answer; one without a status when the service couldn't be reached.</returns>
  private async Task<JsonApiResponse> SendAsync (JsonApiSettings settings, Uri url, CancellationToken cancellationToken)
  {
    try
    {
      HttpClient client = this.httpClientFactory.CreateClient (JsonApiClient.ClientName);

      using var request = new HttpRequestMessage (method: HttpMethod.Get, requestUri: url);
      request.Headers.Accept.Add (new MediaTypeWithQualityHeaderValue ("application/json"));

      if (!JsonApiClient.TryAuthenticate (request: request, settings: settings))
      {
        return JsonApiResponse.Unreachable;
      }

      using HttpResponseMessage response = await client.SendAsync (request: request,
                                                                   completionOption: HttpCompletionOption.ResponseHeadersRead,
                                                                   cancellationToken: cancellationToken);

      if (!response.IsSuccessStatusCode)
      {
        return new JsonApiResponse (StatusCode: response.StatusCode, ReasonPhrase: response.ReasonPhrase, Body: null);
      }

      if (response.Content.Headers.ContentLength > JsonApiClient.MaxBodyBytes)
      {
        return new JsonApiResponse (StatusCode: response.StatusCode, ReasonPhrase: response.ReasonPhrase, Body: null)
               {
                 IsTooLarge = true,
               };
      }

      await using Stream stream = await response.Content.ReadAsStreamAsync (cancellationToken);
      using var          body   = new MemoryStream ();

      // One byte more than the limit tells an answer that fits exactly from one that doesn't.
      var buffer = new byte[16 * 1024];
      int read;

      while ((read = await stream.ReadAsync (buffer: buffer, cancellationToken: cancellationToken)) > 0)
      {
        body.Write (buffer: buffer, offset: 0, count: read);

        if (body.Length > JsonApiClient.MaxBodyBytes)
        {
          return new JsonApiResponse (StatusCode: response.StatusCode, ReasonPhrase: response.ReasonPhrase, Body: null)
                 {
                   IsTooLarge = true,
                 };
        }
      }

      return new JsonApiResponse (StatusCode: response.StatusCode, ReasonPhrase: response.ReasonPhrase, Body: body.ToArray ());
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      return JsonApiResponse.Unreachable;
    }
  }

  /// <summary>
  ///   Adds a connection's authentication to a request.
  /// </summary>
  /// <param name="request">The request.</param>
  /// <param name="settings">The connection's settings.</param>
  /// <returns><c>false</c> when the settings can't authenticate a request, such as a header name HTTP doesn't allow.</returns>
  private static bool TryAuthenticate (HttpRequestMessage request, JsonApiSettings settings)
  {
    string? credential = settings.Credential;

    // A control character, a newline above all, would let a credential add headers of its own.
    if (credential?.Any (char.IsControl) == true)
    {
      return false;
    }

    switch (settings.Auth)
    {
      case JsonApiAuth.Basic when credential != null:
        request.Headers.Authorization =
          new AuthenticationHeaderValue (scheme: "Basic",
                                         parameter:
                                         Convert.ToBase64String (Encoding.UTF8.GetBytes ($"{settings.Username}:{credential}")));

        return true;

      case JsonApiAuth.Bearer when credential != null:
        request.Headers.Authorization = new AuthenticationHeaderValue (scheme: "Bearer", parameter: credential);

        return true;

      case JsonApiAuth.Header when credential != null:
        return request.Headers.TryAddWithoutValidation (name: settings.HeaderName, value: credential);

      case JsonApiAuth.None:
        return true;

      default:
        return false;
    }
  }
}

/// <summary>
///   What a JSON API answered.
/// </summary>
/// <param name="StatusCode">The HTTP status, or <c>null</c> when the service couldn't be reached.</param>
/// <param name="ReasonPhrase">The status's reason phrase, if the service gave one.</param>
/// <param name="Body">The answer's content, when the status was a success and it was within the size limit.</param>
public sealed record JsonApiResponse (HttpStatusCode? StatusCode, string? ReasonPhrase, byte[]? Body)
{
  /// <summary>
  ///   Gets the response of a service that couldn't be reached: a refused connection, a failed TLS handshake or a timeout.
  /// </summary>
  public static JsonApiResponse Unreachable { get; } = new (StatusCode: null, ReasonPhrase: null, Body: null);

  /// <summary>
  ///   Gets a value indicating whether the service answered successfully, but with more than
  ///   <see cref="JsonApiClient.MaxBodyBytes" />.
  /// </summary>
  public bool IsTooLarge { get; init; }

  /// <summary>
  ///   Gets a value indicating whether the service couldn't be reached.
  /// </summary>
  public bool IsUnreachable => this.StatusCode == null;

  /// <summary>
  ///   Gets a value indicating whether the service answered successfully with content within the size limit.
  /// </summary>
  public bool IsSuccess => this.Body != null;

  /// <summary>
  ///   Gets the status as a sentence would name it, such as <c>503 Service Unavailable</c>.
  /// </summary>
  public string Status => this.StatusCode is { } code ? $"{(int)code} {this.ReasonPhrase}".Trim () : "no answer";
}

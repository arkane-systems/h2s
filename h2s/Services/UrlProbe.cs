using System.Net;
using System.Security.Authentication;

namespace h2s.Services;

/// <summary>
///   Checks whether a URL is reachable, distinguishing a server whose certificate isn't trusted from one that can't be
///   reached at all.
/// </summary>
public class UrlProbe
{
  /// <summary>
  ///   The name of the HTTP client used for ordinary probes.
  /// </summary>
  public const string ClientName = "UrlProbe";

  /// <summary>
  ///   The name of the HTTP client that accepts any server certificate. It is used only to re-probe a server whose
  ///   certificate failed validation, and never to fetch content.
  /// </summary>
  public const string AcceptAnyCertificateClientName = "UrlProbe.AcceptAnyCertificate";

  /// <summary>
  ///   The time allowed for each probe request.
  /// </summary>
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

  private readonly IHttpClientFactory httpClientFactory;

  /// <summary>
  ///   Initializes a new instance of the <see cref="UrlProbe" /> class.
  /// </summary>
  /// <param name="httpClientFactory">The factory that provides the named probe clients.</param>
  public UrlProbe (IHttpClientFactory httpClientFactory) => this.httpClientFactory = httpClientFactory;

  /// <summary>
  ///   Checks whether a URL is reachable using an HTTP HEAD request, falling back to GET if HEAD is not supported.
  /// </summary>
  /// <param name="url">The URL to check.</param>
  /// <param name="cancellationToken">A token that cancels the check.</param>
  /// <returns>A <see cref="UrlReachability" /> value describing the outcome.</returns>
  public async Task<UrlReachability> CheckAsync (string url, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull (url);

    try
    {
      return await UrlProbe.SendProbeAsync (client: this.httpClientFactory.CreateClient (UrlProbe.ClientName),
                                            url: url,
                                            cancellationToken: cancellationToken)
               ? UrlReachability.Reachable
               : UrlReachability.Unreachable;
    }
    catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
    {
      // The TLS handshake failed. Probe again without certificate validation to tell an untrusted internal CA apart
      // from a genuine network failure.
      return await this.ProbeWithoutCertificateValidationAsync (url: url, cancellationToken: cancellationToken);
    }
    catch
    {
      return UrlReachability.Unreachable;
    }
  }

  /// <summary>
  ///   Repeats the probe with the client that accepts any certificate, to confirm that the server is up when the
  ///   ordinary probe failed certificate validation.
  /// </summary>
  /// <param name="url">The URL to check.</param>
  /// <param name="cancellationToken">A token that cancels the check.</param>
  /// <returns>
  ///   <see cref="UrlReachability.UntrustedCertificate" /> when the server responds successfully despite the
  ///   certificate error; <see cref="UrlReachability.Unreachable" /> otherwise.
  /// </returns>
  private async Task<UrlReachability> ProbeWithoutCertificateValidationAsync (string url, CancellationToken cancellationToken)
  {
    try
    {
      HttpClient client = this.httpClientFactory.CreateClient (UrlProbe.AcceptAnyCertificateClientName);

      return await UrlProbe.SendProbeAsync (client: client, url: url, cancellationToken: cancellationToken)
               ? UrlReachability.UntrustedCertificate
               : UrlReachability.Unreachable;
    }
    catch
    {
      return UrlReachability.Unreachable;
    }
  }

  /// <summary>
  ///   Sends a HEAD request, falling back to GET when the server doesn't allow HEAD.
  /// </summary>
  /// <param name="client">The client to send the requests with.</param>
  /// <param name="url">The URL to check.</param>
  /// <param name="cancellationToken">A token that cancels the requests.</param>
  /// <returns><c>true</c> when the server responded with a success status code; otherwise, <c>false</c>.</returns>
  private static async Task<bool> SendProbeAsync (HttpClient client, string url, CancellationToken cancellationToken)
  {
    using var headRequest = new HttpRequestMessage (method: HttpMethod.Head, requestUri: url);
    using HttpResponseMessage headResponse = await client.SendAsync (request: headRequest,
                                                                     completionOption: HttpCompletionOption.ResponseHeadersRead,
                                                                     cancellationToken: cancellationToken);

    if (headResponse.IsSuccessStatusCode)
      return true;

    if (headResponse.StatusCode != HttpStatusCode.MethodNotAllowed)
      return false;

    using var getRequest = new HttpRequestMessage (method: HttpMethod.Get, requestUri: url);
    using HttpResponseMessage getResponse = await client.SendAsync (request: getRequest,
                                                                    completionOption: HttpCompletionOption.ResponseHeadersRead,
                                                                    cancellationToken: cancellationToken);

    return getResponse.IsSuccessStatusCode;
  }
}

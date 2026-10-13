using System.Net;
using System.Security.Authentication;
using h2s.Services;
using h2s.Tests.Infrastructure;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="UrlProbe" />.
/// </summary>
public class UrlProbeTests
{
  private const string Url = "https://kuma.home.arpa";

  [Fact]
  public async Task CheckAsync_FallsBackToGetWhenHeadIsNotAllowed ()
  {
    var handler = new StubHttpMessageHandler (r => new HttpResponseMessage (r.Method == HttpMethod.Head
                                                                              ? HttpStatusCode.MethodNotAllowed
                                                                              : HttpStatusCode.OK));

    UrlReachability result = await UrlProbeTests.CheckAsync (handler);

    Assert.Equal (expected: UrlReachability.Reachable,         actual: result);
    Assert.Equal (expected: [HttpMethod.Head, HttpMethod.Get], actual: handler.Requests.Select (r => r.Method));
  }

  [Fact]
  public async Task CheckAsync_ReportsErrorStatusAsUnreachable ()
    => Assert.Equal (expected: UrlReachability.Unreachable,
                     actual: await UrlProbeTests.CheckAsync (StubHttpMessageHandler.Returning (HttpStatusCode.NotFound)));

  [Fact]
  public async Task CheckAsync_ReportsNetworkFailureAsUnreachable ()
  {
    StubHttpMessageHandler acceptAny = StubHttpMessageHandler.Returning (HttpStatusCode.OK);

    UrlReachability result =
      await UrlProbeTests.CheckAsync (handler: StubHttpMessageHandler.Throwing (new HttpRequestException ("Connection refused")),
                                      acceptAnyCertificateHandler: acceptAny);

    Assert.Equal (expected: UrlReachability.Unreachable, actual: result);
    Assert.Empty (acceptAny.Requests);
  }

  [Fact]
  public async Task CheckAsync_ReportsReachableServer ()
  {
    StubHttpMessageHandler handler = StubHttpMessageHandler.Returning (HttpStatusCode.OK);

    UrlReachability result = await UrlProbeTests.CheckAsync (handler);

    Assert.Equal (expected: UrlReachability.Reachable, actual: result);
    Assert.Equal (expected: HttpMethod.Head,           actual: Assert.Single (handler.Requests).Method);
  }

  [Fact]
  public async Task CheckAsync_ReportsUnreachableWhenRetryWithoutValidationFails ()
    => Assert.Equal (expected: UrlReachability.Unreachable,
                     actual: await UrlProbeTests.CheckAsync (handler: UrlProbeTests.TlsFailure (),
                                                             acceptAnyCertificateHandler:
                                                             StubHttpMessageHandler.Returning (HttpStatusCode.ServiceUnavailable)));

  [Fact]
  public async Task CheckAsync_ReportsUntrustedCertificate ()
    => Assert.Equal (expected: UrlReachability.UntrustedCertificate,
                     actual: await UrlProbeTests.CheckAsync (handler: UrlProbeTests.TlsFailure (),
                                                             acceptAnyCertificateHandler:
                                                             StubHttpMessageHandler.Returning (HttpStatusCode.OK)));

  /// <summary>
  ///   Probes <see cref="Url" /> through stub handlers.
  /// </summary>
  /// <param name="handler">The handler behind the ordinary probe client.</param>
  /// <param name="acceptAnyCertificateHandler">The handler behind the accept-any-certificate client.</param>
  /// <returns>The probe result.</returns>
  private static Task<UrlReachability> CheckAsync (HttpMessageHandler  handler,
                                                   HttpMessageHandler? acceptAnyCertificateHandler = null)
  {
    var factory = new StubHttpClientFactory (new Dictionary<string, HttpMessageHandler>
                                             {
                                               [UrlProbe.ClientName] = handler,
                                               [UrlProbe.AcceptAnyCertificateClientName] =
                                                 acceptAnyCertificateHandler ??
                                                 StubHttpMessageHandler.Returning (HttpStatusCode.OK),
                                             });

    return new UrlProbe (factory).CheckAsync (url: UrlProbeTests.Url, cancellationToken: TestContext.Current.CancellationToken);
  }

  /// <summary>
  ///   Creates a handler that fails every request the way a TLS handshake with an untrusted certificate does.
  /// </summary>
  /// <returns>The handler.</returns>
  private static StubHttpMessageHandler TlsFailure ()
    => StubHttpMessageHandler.Throwing (new HttpRequestException (message: "The SSL connection could not be established.",
                                                                  inner: new AuthenticationException ("Untrusted root.")));
}

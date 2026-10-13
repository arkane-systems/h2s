using System.Net;

namespace h2s.Tests.Infrastructure;

/// <summary>
///   An HTTP message handler that answers requests from a callback instead of the network, and records them.
/// </summary>
/// <param name="respond">Produces the response for a request, or throws to simulate a transport failure.</param>
public sealed class StubHttpMessageHandler (Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
  /// <summary>
  ///   Gets the requests received so far, in order.
  /// </summary>
  public List<HttpRequestMessage> Requests { get; } = [];

  /// <summary>
  ///   Creates a handler that answers every request with the given status code.
  /// </summary>
  /// <param name="statusCode">The status code to answer with.</param>
  /// <returns>The handler.</returns>
  public static StubHttpMessageHandler Returning (HttpStatusCode statusCode) => new (_ => new HttpResponseMessage (statusCode));

  /// <summary>
  ///   Creates a handler that fails every request with the given exception.
  /// </summary>
  /// <param name="exception">The exception to throw.</param>
  /// <returns>The handler.</returns>
  public static StubHttpMessageHandler Throwing (Exception exception) => new (_ => throw exception);

  /// <inheritdoc />
  protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested ();
    this.Requests.Add (request);

    return Task.FromResult (respond (request));
  }
}

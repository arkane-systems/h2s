namespace h2s.Tests.Infrastructure;

/// <summary>
///   An <see cref="IHttpClientFactory" /> whose named clients send through stub handlers.
/// </summary>
/// <param name="handlers">The handler for each client name.</param>
public sealed class StubHttpClientFactory (IReadOnlyDictionary<string, HttpMessageHandler> handlers) : IHttpClientFactory
{
  /// <inheritdoc />
  public HttpClient CreateClient (string name) => new (handler: handlers[name], disposeHandler: false);
}

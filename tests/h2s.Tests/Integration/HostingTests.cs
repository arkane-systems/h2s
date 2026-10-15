using System.Net;
using h2s.Authorization;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for hosting concerns: health checks, the admin authorization seam and reverse-proxy support.
/// </summary>
public class HostingTests
{
  [Theory]
  [InlineData ("Admin/Editor")]
  [InlineData ("Admin/Settings")]
  public async Task AdminPages_RequireAdminPolicy (string route)
  {
    await using var factory = new DashboardApplicationFactory ();

    RouteEndpoint endpoint = HostingTests.FindPageEndpoint (factory: factory, route: route);

    Assert.Contains (collection: endpoint.Metadata.GetOrderedMetadata<IAuthorizeData> (),
                     filter: a => a.Policy == AdminRequirement.PolicyName);
  }

  [Fact]
  public async Task ForwardedHeaders_TrustAnyProxy ()
  {
    await using var factory = new DashboardApplicationFactory ();

    ForwardedHeadersOptions options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>> ().Value;

    Assert.Equal (expected: ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, actual: options.ForwardedHeaders);
    Assert.Empty (options.KnownIPNetworks);
    Assert.Empty (options.KnownProxies);
  }

  [Theory]
  [InlineData (IconCatalog.ClientName,                  15)]
  [InlineData (PageMetadataReader.ClientName,           5)]
  [InlineData (UptimeKumaClient.ClientName,             5)]
  [InlineData (UrlProbe.ClientName,                     5)]
  [InlineData (UrlProbe.AcceptAnyCertificateClientName, 5)]
  public async Task HttpClients_AreRegisteredWithTimeouts (string name, int timeoutSeconds)
  {
    await using var factory = new DashboardApplicationFactory ();

    HttpClient client = factory.Services.GetRequiredService<IHttpClientFactory> ().CreateClient (name);

    Assert.Equal (expected: TimeSpan.FromSeconds (timeoutSeconds), actual: client.Timeout);
  }

  [Fact]
  public async Task Healthz_ReportsHealthy ()
  {
    await using var factory = new DashboardApplicationFactory ();

    using HttpResponseMessage response = await factory.CreateClient ()
                                                      .GetAsync (requestUri: "/healthz",
                                                                 cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.Equal (expected: "Healthy",
                  actual: await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task IndexPage_IsNotRestricted ()
  {
    await using var factory = new DashboardApplicationFactory ();

    RouteEndpoint endpoint = HostingTests.FindPageEndpoint (factory: factory, route: "Index");

    Assert.Empty (endpoint.Metadata.GetOrderedMetadata<IAuthorizeData> ());
  }

  /// <summary>
  ///   Finds the endpoint serving a Razor page at a route.
  /// </summary>
  /// <param name="factory">The factory hosting the application.</param>
  /// <param name="route">The page route pattern, without a leading slash.</param>
  /// <returns>The page's endpoint.</returns>
  private static RouteEndpoint FindPageEndpoint (DashboardApplicationFactory factory, string route)
    => factory.Services.GetRequiredService<EndpointDataSource> ()
              .Endpoints.OfType<RouteEndpoint> ()
              .First (e => e.RoutePattern.RawText == route);
}

using System.Security.Claims;
using h2s.Data;
using h2s.Integrations;
using h2s.Models;
using h2s.Pages;
using h2s.Panels;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for the link helpers on <see cref="IndexModel" />.
/// </summary>
public class IndexModelTests
{
  [Theory]
  [InlineData ("https://jellyfin.home.arpa/",        false)]
  [InlineData ("https://home.arpa/",                 false)]
  [InlineData ("https://JELLYFIN.HOME.ARPA/",        false)]
  [InlineData ("https://wiki.corp.example.com/page", false)]
  [InlineData ("https://github.com/",                true)]
  [InlineData ("https://evilhome.arpa/",             true)]
  [InlineData ("https://example.com/",               true)]
  [InlineData ("not a url",                          false)]
  public async Task IsExternalLink_UsesLocalDomains (string url, bool expected)
  {
    IndexModel model = await IndexModelTests.LoadAsync (localDomains: "https://Home.Arpa/, .corp.example.com");

    Assert.Equal (expected: expected, actual: model.IsExternalLink (url));
  }

  [Fact]
  public async Task CreateLinkTile_FlagsExternalLinks ()
  {
    IndexModel model = await IndexModelTests.LoadAsync (localDomains: "home.arpa");

    Assert.True (model.CreateLinkTile (new Link { Label = "GitHub", Url = "https://github.com/" }).IsExternal);
  }

  [Fact]
  public async Task IsExternalLink_IsFalseWithoutLocalDomains ()
  {
    IndexModel model = await IndexModelTests.LoadAsync (localDomains: "");

    Assert.False (model.IsExternalLink ("https://github.com/"));
  }

  /// <summary>
  ///   Creates an <see cref="IndexModel" /> over a fresh database with the given settings and runs its GET handler.
  /// </summary>
  /// <param name="localDomains">The local domains setting.</param>
  /// <returns>The loaded page model.</returns>
  private static async Task<IndexModel> LoadAsync (string localDomains)
  {
    await using var              database = await TestDatabase.CreateAsync ();
    await using DashboardContext context  = database.CreateContext ();

    await context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression:
                                                                                              x => x.LocalDomains,
                                                                                              valueExpression: localDomains),
                                                        cancellationToken: TestContext.Current.CancellationToken);

    using var         cache           = new MemoryCache (new MemoryCacheOptions ());
    var               settingsService = new DashboardSettingsService (context: context, cache: cache);
    ConnectionService connections     = TestConnections.CreateService (context: context, cache: cache);

    await using ServiceProvider services = new ServiceCollection ().AddSingleton (context)
                                                                   .AddSingleton (settingsService)
                                                                   .AddSingleton (connections)
                                                                   .BuildServiceProvider ();

    var model = new IndexModel (context: context,
                                settingsService: settingsService,
                                panelService: new PanelService (context: context,
                                                                catalog: new PanelCatalog ([
                                                                                             new StatusBarPanelKind (),
                                                                                             new SearchPanelKind (),
                                                                                           ]),
                                                                services: services,
                                                                logger: NullLogger<PanelService>.Instance),
                                authorizationService: new AllowAllAuthorizationService (),
                                connections: connections,
                                widgetService: TestConnections.CreateWidgetService (context: context,
                                                                                    connections: connections,
                                                                                    cache: cache,
                                                                                    services: services))
                {
                  PageContext = new PageContext { HttpContext = new DefaultHttpContext () },
                };
    await model.OnGetAsync (cancellationToken: TestContext.Current.CancellationToken);

    return model;
  }

  /// <summary>
  ///   An authorization service that grants everything. These tests never request edit mode, the only thing it guards.
  /// </summary>
  private sealed class AllowAllAuthorizationService : IAuthorizationService
  {
    /// <inheritdoc />
    public Task<AuthorizationResult> AuthorizeAsync (ClaimsPrincipal                        user,
                                                     object?                                resource,
                                                     IEnumerable<IAuthorizationRequirement> requirements)
      => Task.FromResult (AuthorizationResult.Success ());

    /// <inheritdoc />
    public Task<AuthorizationResult> AuthorizeAsync (ClaimsPrincipal user, object? resource, string policyName)
      => Task.FromResult (AuthorizationResult.Success ());
  }
}

using System.Net;
using System.Text.RegularExpressions;
using h2s.Authorization;
using h2s.Models;
using h2s.Panels;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace h2s.Tests.Integration;

/// <summary>
///   Tests for the public dashboard page.
/// </summary>
public partial class IndexPageTests
{
  [Fact]
  public async Task Index_FlagsExternalLinks ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    // The seeded Reference category holds the three links outside home.arpa.
    Assert.Equal (expected: 3, actual: IndexPageTests.ExternalFlagPattern ().Count (html));
  }

  [Fact]
  public async Task Index_ShowsVersionInFooter ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    // The layout's CSS isolation adds a scope attribute (b-…) to every tag.
    Assert.Matches (expectedRegexPattern: $"""<span[^>]*data-role="app-version"[^>]*>{Regex.Escape (AppVersion.Current)}</span>""",
                    actualString: html);
  }

  [Fact]
  public async Task Index_HidesAnEmptyStatusBar ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    // The seeded status bar has no items, so it isn't shown.
    Assert.DoesNotContain (expectedSubstring: "status-bar-items",  actualString: html);
    Assert.DoesNotContain (expectedSubstring: "link-status-badge", actualString: html);
  }

  [Fact]
  public async Task Index_ListsRegularCategoriesBeforeAdminCategoriesInSortOrder ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);
    IEnumerable<string> names = IndexPageTests.CategoryHeadingPattern ().Matches (html).Select (m => m.Groups["name"].Value);

    // The seeder's sort orders aren't alphabetical, so this shows SortOrder rather than Name decides.
    Assert.Equal (expected: ["Media", "Home", "Reference", "Infrastructure", "Network"], actual: names);
    Assert.Contains (expectedSubstring: "category-grid-break", actualString: html);
  }

  [Fact]
  public async Task Index_ListsLinksInSortOrder ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);
    IEnumerable<string> labels = IndexPageTests.LinkTitlePattern ().Matches (html).Select (m => m.Groups["label"].Value);

    Assert.Equal (expected: ["Jellyfin", "Sonarr", "Radarr", "Audiobookshelf"], actual: labels.Take (4));
  }

  [Fact]
  public async Task Index_ShowsNoEditControlsOutsideEditMode ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.DoesNotContain (expectedSubstring: "drag-handle",                                          actualString: html);
    Assert.DoesNotContain (expectedSubstring: "data-reorder",                                         actualString: html);
    Assert.DoesNotContain (expectedSubstring: """<script type="module" src="/js/modules/edit-mode""", actualString: html);
    Assert.Contains (expectedSubstring: """href="/?edit=true""", actualString: html);
  }

  [Fact]
  public async Task Index_EditModeRendersDragHandlesAndReorderUrls ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/?edit=true",
                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: 5,  actual: IndexPageTests.CategoryDragHandlePattern ().Count (html));
    Assert.Equal (expected: 17, actual: IndexPageTests.LinkDragHandlePattern ().Count (html));
    Assert.Contains (expectedSubstring: """data-reorder-categories-url="/Admin/Editor?handler=ReorderCategories""",
                     actualString: html);
    Assert.Contains (expectedSubstring: """data-reorder-links-url="/Admin/Editor?handler=ReorderLinks""",   actualString: html);
    Assert.Contains (expectedSubstring: """data-reorder-panels-url="/Admin/Editor?handler=ReorderPanels""", actualString: html);
    Assert.Matches (expectedRegexPattern: """<script type="module" src="/js/modules/edit-mode\.[^"]*js""", actualString: html);
    Assert.Contains (expectedSubstring: ">Done</a>", actualString: html);
  }

  [Fact]
  public async Task Index_IgnoresEditModeWithoutAdminAccess ()
  {
    await using var factory = new DashboardApplicationFactory ();
    await using WebApplicationFactory<Program> denied =
      factory.WithWebHostBuilder (builder => builder.ConfigureTestServices (services => services
                                                                             .AddSingleton<IAuthorizationHandler,
                                                                                DenyAdminHandler> ()));

    string html = await denied.CreateClient ()
                              .GetStringAsync (requestUri: "/?edit=true", cancellationToken: TestContext.Current.CancellationToken);

    Assert.DoesNotContain (expectedSubstring: "drag-handle",  actualString: html);
    Assert.DoesNotContain (expectedSubstring: "data-reorder", actualString: html);
  }

  [Theory]
  [InlineData ("""{"title":"Home","linkUrl":"https://status.home.arpa/status/main"}""", true)]
  [InlineData ("{}",                                                                    false)]
  public async Task Index_RendersTheStatusBarsItemsAsWidgets (string optionsJson, bool isLink)
  {
    await using var factory = new DashboardApplicationFactory ();
    await factory.AddUptimeKumaAsync (apiKey: TestConnections.KumaApiKey);
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Panel bar = await context.Panels.SingleAsync (predicate: p => p.Kind == StatusBarPanelKind.KindKey,
                                                    cancellationToken: TestContext.Current.CancellationToken);
      Connection kuma = await context.Connections.SingleAsync (TestContext.Current.CancellationToken);

      bar.OptionsJson = optionsJson;
      bar.Widgets.Add (new Widget
                       {
                         ConnectionId = kuma.Id, Kind = "monitor", OptionsJson = """{"monitorId":1}""", Label = "Compute",
                       });
      await context.SaveChangesAsync (TestContext.Current.CancellationToken);
    }

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """<div class="status-bar-label">Compute</div>""", actualString: html);
    Assert.Contains (expectedSubstring: "widget-pending",                                  actualString: html);
    Assert.Equal (expected: isLink,  actual: html.Contains ("""href="https://status.home.arpa/status/main"""));
    Assert.Equal (expected: isLink,  actual: html.Contains ("""<div class="status-bar-title">Home</div>"""));
    Assert.Equal (expected: !isLink, actual: html.Contains ("status-bar-static"));

    // Kuma's badge images are gone, from links and the status bar alike.
    Assert.DoesNotContain (expectedSubstring: "/api/badge/", actualString: html);
  }

  [Fact]
  public async Task Index_LoadsClientScriptsLocally ()
  {
    await using var factory = new DashboardApplicationFactory ();
    HttpClient      client  = factory.CreateClient ();

    string html = await client.GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Matches (expectedRegexPattern: """<script src="/js/api\.[^"]*js""",     actualString: html);
    Assert.Matches (expectedRegexPattern: """<script src="/js/lowpoly\.[^"]*js""", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "trianglify", actualString: html, comparisonType: StringComparison.OrdinalIgnoreCase);
    Assert.Single (IndexPageTests.AntiforgeryFieldPattern ().Matches (html));

    using HttpResponseMessage script = await client.GetAsync (requestUri: "/js/lowpoly.js",
                                                              cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: script.StatusCode);
  }

  [Fact]
  public async Task Index_RendersCategoriesCollapsedFromTheCookie ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context) = factory.CreateContext ();
    int media, home;

    using (scope)
    {
      media = await context.Categories.Where (c => c.Name == "Media")
                           .Select (c => c.Id)
                           .SingleAsync (TestContext.Current.CancellationToken);
      home = await context.Categories.Where (c => c.Name == "Home")
                          .Select (c => c.Id)
                          .SingleAsync (TestContext.Current.CancellationToken);
    }

    HttpClient client = factory.CreateClient ();

    string html = await IndexPageTests.GetWithCookieAsync (client: client,
                                                           path: "/",
                                                           cookie: $"h2s-collapsed={media}.junk.-3.{home}");

    Assert.Matches (expectedRegexPattern: $"""<section class="category-box border  is-collapsed"\s+data-category-id="{media}""",
                    actualString: html);
    Assert.Matches (expectedRegexPattern: $"""aria-expanded="false"\s+aria-controls="category-body-{home}""", actualString: html);
    Assert.Equal (expected: 2, actual: IndexPageTests.CollapsedPattern ().Count (html));
    Assert.Matches (expectedRegexPattern: """<script type="module" src="/js/modules/category-collapse\.[^"]*js""",
                    actualString: html);

    // Edit mode shows everything, with the plain heading.
    string edit = await IndexPageTests.GetWithCookieAsync (client: client, path: "/?edit=true", cookie: $"h2s-collapsed={media}");

    Assert.DoesNotContain (expectedSubstring: "is-collapsed",             actualString: edit);
    Assert.DoesNotContain (expectedSubstring: "category-collapse-button", actualString: edit);
  }

  [Fact]
  public async Task Index_RendersEveryCategoryExpandedWithoutTheCookie ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: 0, actual: IndexPageTests.CollapsedPattern ().Count (html));
    Assert.Equal (expected: 5, actual: IndexPageTests.ExpandedPattern ().Count (html));
  }

  /// <summary>
  ///   Gets a page's HTML with a cookie.
  /// </summary>
  /// <param name="client">The client.</param>
  /// <param name="path">The page.</param>
  /// <param name="cookie">The <c>Cookie</c> header's value.</param>
  /// <returns>The HTML.</returns>
  private static async Task<string> GetWithCookieAsync (HttpClient client, string path, string cookie)
  {
    using var request = new HttpRequestMessage (method: HttpMethod.Get, requestUri: path);
    request.Headers.Add (name: "Cookie", value: cookie);

    using HttpResponseMessage response = await client.SendAsync (request: request,
                                                                 cancellationToken: TestContext.Current.CancellationToken);

    return await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);
  }

  [Fact]
  public async Task Index_UsesSettingsForTitle ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "<title>h2s - Development</title>", actualString: html);
    Assert.Contains (expectedSubstring: "Seeded sample data",               actualString: html);
  }

  // Edit mode renders the name in the heading; elsewhere it's inside the heading's collapse button.
  [GeneratedRegex ("""(?:<h2 class="h5 mb-0">|<span class="category-name">)(?<name>[^<]+)<""")]
  private static partial Regex CategoryHeadingPattern ();

  [GeneratedRegex ("""<span class="link-block-title">(?<label>[^<]+)</span>""")]
  private static partial Regex LinkTitlePattern ();

  [GeneratedRegex ("""class="drag-handle category-drag-handle""")]
  private static partial Regex CategoryDragHandlePattern ();

  [GeneratedRegex ("""class="drag-handle link-drag-handle""")]
  private static partial Regex LinkDragHandlePattern ();

  [GeneratedRegex ("""name="__RequestVerificationToken""")]
  private static partial Regex AntiforgeryFieldPattern ();

  [GeneratedRegex ("""class="category-collapse-button" aria-expanded="false""")]
  private static partial Regex CollapsedPattern ();

  [GeneratedRegex ("""class="category-collapse-button" aria-expanded="true""")]
  private static partial Regex ExpandedPattern ();

  [GeneratedRegex ("""class="link-external-flag""")]
  private static partial Regex ExternalFlagPattern ();

  /// <summary>
  ///   Denies the Admin requirement, standing in for a user the homelab-wide authentication doesn't make an admin.
  /// </summary>
  private sealed class DenyAdminHandler : AuthorizationHandler<AdminRequirement>
  {
    /// <inheritdoc />
    protected override Task HandleRequirementAsync (AuthorizationHandlerContext context, AdminRequirement requirement)
    {
      context.Fail ();

      return Task.CompletedTask;
    }
  }
}

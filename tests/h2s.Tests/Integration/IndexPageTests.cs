using System.Net;
using System.Text.RegularExpressions;
using h2s.Authorization;
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
  public async Task Index_HidesKumaFeaturesWhenUnconfigured ()
  {
    await using var factory = new DashboardApplicationFactory ();

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.DoesNotContain (expectedSubstring: "uptime-status-bar", actualString: html);
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
    Assert.Contains (expectedSubstring: """data-reorder-links-url="/Admin/Editor?handler=ReorderLinks""", actualString: html);
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

  [Fact]
  public async Task Index_RendersKumaBadgesWhenConfigured ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      await context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls: s => s
                                                                                .SetProperty (propertyExpression:
                                                                                              x => x.UptimeKumaServerUrl,
                                                                                              valueExpression:
                                                                                              "https://status.home.arpa")
                                                                                .SetProperty (propertyExpression:
                                                                                              x => x.UptimeKumaStatusPageSlug,
                                                                                              valueExpression: "main"),
                                                          cancellationToken: TestContext.Current.CancellationToken);
    }

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: """href="https://status.home.arpa/status/main""",                     actualString: html);
    Assert.Contains (expectedSubstring: "https://status.home.arpa/api/badge/1/uptime/24?style=flat",          actualString: html);
    Assert.Contains (expectedSubstring: "https://status.home.arpa/api/badge/11/status?style=flat&amp;label=", actualString: html);
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
  public async Task Index_StatusBarIsStaticWithoutStatusPageSlug ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      await context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression:
                                                                                                x => x.UptimeKumaServerUrl,
                                                                                                valueExpression:
                                                                                                "https://status.home.arpa"),
                                                          cancellationToken: TestContext.Current.CancellationToken);
    }

    string html = await factory.CreateClient ()
                               .GetStringAsync (requestUri: "/", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "uptime-status-bar-static", actualString: html);
    Assert.DoesNotContain (expectedSubstring: "uptime-status-bar-link", actualString: html);
    Assert.Contains (expectedSubstring: "https://status.home.arpa/api/badge/1/uptime/24?style=flat", actualString: html);
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

  [GeneratedRegex ("""<h2 class="h5 mb-0">(?<name>[^<]+)</h2>""")]
  private static partial Regex CategoryHeadingPattern ();

  [GeneratedRegex ("""<span class="link-block-title">(?<label>[^<]+)</span>""")]
  private static partial Regex LinkTitlePattern ();

  [GeneratedRegex ("""class="drag-handle category-drag-handle""")]
  private static partial Regex CategoryDragHandlePattern ();

  [GeneratedRegex ("""class="drag-handle link-drag-handle""")]
  private static partial Regex LinkDragHandlePattern ();

  [GeneratedRegex ("""name="__RequestVerificationToken""")]
  private static partial Regex AntiforgeryFieldPattern ();

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

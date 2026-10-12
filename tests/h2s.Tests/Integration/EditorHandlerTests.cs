using System.Net;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Integration;

/// <summary>
/// Tests for the JSON page handlers behind the admin editor.
/// </summary>
public class EditorHandlerTests
{
  private const string EditorPath = "/Admin/Editor";

  [Fact]
  public async Task CreateCategory_AddsCategory ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var client              = factory.CreateClient ();

    var response = await FormPoster.PostAsync (client, EditorHandlerTests.EditorPath, "CreateCategory", [new ("name", "  Games  "), new ("isAdminCategory", "true")]);

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var json = await FormPoster.ReadJsonAsync (response);

    Assert.Equal ("Games", json.GetProperty ("name").GetString ());
    Assert.True (json.GetProperty ("isAdminCategory").GetBoolean ());

    var categories = await FormPoster.ReadJsonAsync (await client.GetAsync ($"{EditorHandlerTests.EditorPath}?handler=Categories",
                                                                            TestContext.Current.CancellationToken));

    Assert.Contains (categories.EnumerateArray (), c => c.GetProperty ("name").GetString () == "Games");
  }

  [Fact]
  public async Task CreateCategory_RejectsBlankName ()
  {
    await using var factory = new DashboardApplicationFactory ();

    var response = await FormPoster.PostAsync (factory.CreateClient (), EditorHandlerTests.EditorPath, "CreateCategory", [new ("name", "   ")]);

    Assert.Equal (HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task CreateLink_AddsLinkWithNormalizedIcon ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var categoryId          = await EditorHandlerTests.GetCategoryIdAsync (factory, "Media");

    var response = await FormPoster.PostAsync (factory.CreateClient (),
                                               EditorHandlerTests.EditorPath,
                                               "CreateLink",
                                               [
                                                 new ("categoryId", categoryId.ToString ()),
                                                 new ("label", "Navidrome"),
                                                 new ("url", "https://music.home.arpa/"),
                                                 new ("iconName", "Navidrome"),
                                                 new ("monitorId", "14"),
                                               ]);

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var json = await FormPoster.ReadJsonAsync (response);

    Assert.Equal ("Media", json.GetProperty ("categoryName").GetString ());
    Assert.Equal ("navidrome", json.GetProperty ("iconName").GetString ());
    Assert.Equal ("14", json.GetProperty ("monitorId").GetString ());
  }

  [Theory]
  [InlineData ("", "https://music.home.arpa/", "")]
  [InlineData ("Navidrome", "", "")]
  [InlineData ("Navidrome", "ftp://music.home.arpa/", "")]
  [InlineData ("Navidrome", "https://music.home.arpa/", "0")]
  public async Task CreateLink_RejectsInvalidInput (string label, string url, string monitorId)
  {
    await using var factory = new DashboardApplicationFactory ();
    var categoryId          = await EditorHandlerTests.GetCategoryIdAsync (factory, "Media");

    var response = await FormPoster.PostAsync (factory.CreateClient (),
                                               EditorHandlerTests.EditorPath,
                                               "CreateLink",
                                               [
                                                 new ("categoryId", categoryId.ToString ()),
                                                 new ("label", label),
                                                 new ("url", url),
                                                 new ("monitorId", monitorId),
                                               ]);

    Assert.Equal (HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task DeleteCategory_RemovesItsLinks ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var categoryId          = await EditorHandlerTests.GetCategoryIdAsync (factory, "Reference");

    var response = await FormPoster.PostAsync (factory.CreateClient (), EditorHandlerTests.EditorPath, "DeleteCategory", [new ("id", categoryId.ToString ())]);

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      Assert.False (await context.Categories.AnyAsync (c => c.Id == categoryId, TestContext.Current.CancellationToken));
      Assert.False (await context.Links.AnyAsync (l => l.CategoryId == categoryId, TestContext.Current.CancellationToken));
    }
  }

  [Fact]
  public async Task Post_RequiresAntiforgeryToken ()
  {
    await using var factory = new DashboardApplicationFactory ();

    var response = await factory.CreateClient ()
                                .PostAsync ($"{EditorHandlerTests.EditorPath}?handler=CreateCategory",
                                            new FormUrlEncodedContent ([new ("name", "Sneaky")]),
                                            TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task UpdateLink_ReturnsNotFoundForUnknownLink ()
  {
    await using var factory = new DashboardApplicationFactory ();
    var categoryId          = await EditorHandlerTests.GetCategoryIdAsync (factory, "Media");

    var response = await FormPoster.PostAsync (factory.CreateClient (),
                                               EditorHandlerTests.EditorPath,
                                               "UpdateLink",
                                               [
                                                 new ("id", "99999"),
                                                 new ("categoryId", categoryId.ToString ()),
                                                 new ("label", "Ghost"),
                                                 new ("url", "https://ghost.home.arpa/"),
                                               ]);

    Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
  }

  /// <summary>
  /// Looks up a seeded category's ID by name.
  /// </summary>
  /// <param name="factory">The factory hosting the database.</param>
  /// <param name="name">The category name.</param>
  /// <returns>The category ID.</returns>
  private static async Task<int> GetCategoryIdAsync (DashboardApplicationFactory factory, string name)
  {
    var (scope, context) = factory.CreateContext ();

    using (scope)
    {
      return await context.Categories.Where (c => c.Name == name).Select (c => c.Id).SingleAsync (TestContext.Current.CancellationToken);
    }
  }
}

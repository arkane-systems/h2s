using h2s.Data;
using h2s.Data.Seeding;
using h2s.Models;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="DevDataSeeder" />.
/// </summary>
public class DevDataSeederTests
{
  [Fact]
  public async Task SeedAsync_FillsMigratedDatabase ()
  {
    await using var              database          = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context           = database.CreateContext ();
    CancellationToken            cancellationToken = TestContext.Current.CancellationToken;

    DashboardSettings settings = await context.DashboardSettings.SingleAsync (cancellationToken);

    Assert.Equal (expected: "Development",             actual: settings.Title);
    Assert.Equal (expected: DevDataSeeder.LocalDomain, actual: settings.LocalDomains);
    Assert.Empty (await context.Connections.ToListAsync (cancellationToken));
    Assert.Contains (collection: await context.Categories.ToListAsync (cancellationToken), filter: c => c.IsAdminCategory);
    Assert.Contains (collection: await context.Categories.ToListAsync (cancellationToken), filter: c => !c.IsAdminCategory);
    Assert.Contains (collection: await context.Links.ToListAsync (cancellationToken),      filter: l => l.IconName == "");
    Assert.Empty (await context.Widgets.ToListAsync (cancellationToken));
  }

  [Fact]
  public async Task SeedAsync_RefusesNonEmptyDatabase ()
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();

    await Assert.ThrowsAsync<InvalidOperationException> (() => DevDataSeeder.SeedAsync (context: context,
                                                                                        cancellationToken: TestContext.Current
                                                                                                                      .CancellationToken));
  }
}

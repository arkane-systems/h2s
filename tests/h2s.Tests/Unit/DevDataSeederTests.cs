using h2s.Data.Seeding;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Unit;

/// <summary>
/// Tests for <see cref="DevDataSeeder"/>.
/// </summary>
public class DevDataSeederTests
{
  [Fact]
  public async Task SeedAsync_FillsMigratedDatabase ()
  {
    await using var database = await TestDatabase.CreateAsync (seed: true);
    await using var context  = database.CreateContext ();
    var cancellationToken    = TestContext.Current.CancellationToken;

    var settings = await context.DashboardSettings.SingleAsync (cancellationToken);

    Assert.Equal ("Development", settings.Title);
    Assert.Equal (DevDataSeeder.LocalDomain, settings.LocalDomains);
    Assert.Null (settings.UptimeKumaServerUrl);
    Assert.Contains (await context.Categories.ToListAsync (cancellationToken), c => c.IsAdminCategory);
    Assert.Contains (await context.Categories.ToListAsync (cancellationToken), c => !c.IsAdminCategory);
    Assert.Contains (await context.Links.ToListAsync (cancellationToken), l => l.IconName == "");
    Assert.Contains (await context.Links.ToListAsync (cancellationToken), l => l.MonitorId is not null);
    Assert.NotEmpty (await context.InfrastructureGroups.ToListAsync (cancellationToken));
  }

  [Fact]
  public async Task SeedAsync_RefusesNonEmptyDatabase ()
  {
    await using var database = await TestDatabase.CreateAsync (seed: true);
    await using var context  = database.CreateContext ();

    await Assert.ThrowsAsync<InvalidOperationException> (() => DevDataSeeder.SeedAsync (context, TestContext.Current.CancellationToken));
  }
}

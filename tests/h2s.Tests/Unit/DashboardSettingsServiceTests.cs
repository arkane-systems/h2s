using h2s.Models;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Unit;

/// <summary>
/// Tests for <see cref="DashboardSettingsService"/>.
/// </summary>
public class DashboardSettingsServiceTests
{
  [Fact]
  public async Task GetSettingsAsync_CreatesDefaultsWhenMissing ()
  {
    await using var database = await TestDatabase.CreateAsync ();
    var cancellationToken    = TestContext.Current.CancellationToken;

    await using (var context = database.CreateContext ())
    {
      await context.DashboardSettings.ExecuteDeleteAsync (cancellationToken);
    }

    await using (var context = database.CreateContext ())
    {
      var settings = await new DashboardSettingsService (context).GetSettingsAsync ();

      Assert.Equal (1, settings.Id);
      Assert.Equal ("Dashboard", settings.Title);
      Assert.Equal ("", settings.Motto);
      Assert.Equal ("", settings.LocalDomains);
      Assert.Equal (ColorMode.Auto, settings.ColorMode);
      Assert.Equal (24, settings.UptimeKumaDefaultDuration);
    }

    await using (var context = database.CreateContext ())
    {
      Assert.Equal (1, await context.DashboardSettings.CountAsync (cancellationToken));
    }
  }

  [Theory]
  [InlineData (0)]
  [InlineData (-5)]
  public async Task GetSettingsAsync_NormalizesInvalidDuration (int storedDuration)
  {
    await using var database = await TestDatabase.CreateAsync ();
    var cancellationToken    = TestContext.Current.CancellationToken;

    await using (var context = database.CreateContext ())
    {
      await context.DashboardSettings.ExecuteUpdateAsync (s => s.SetProperty (x => x.UptimeKumaDefaultDuration, storedDuration),
                                                          cancellationToken);
    }

    await using (var context = database.CreateContext ())
    {
      Assert.Equal (24, (await new DashboardSettingsService (context).GetSettingsAsync ()).UptimeKumaDefaultDuration);
    }

    await using (var context = database.CreateContext ())
    {
      Assert.Equal (24, (await context.DashboardSettings.SingleAsync (cancellationToken)).UptimeKumaDefaultDuration);
    }
  }

  [Fact]
  public async Task SaveSettingsAsync_PersistsChanges ()
  {
    await using var database = await TestDatabase.CreateAsync ();

    await using (var context = database.CreateContext ())
    {
      var service  = new DashboardSettingsService (context);
      var settings = await service.GetSettingsAsync ();

      settings.Title     = "Homelab";
      settings.ColorMode = ColorMode.Dark;
      await service.SaveSettingsAsync (settings);
    }

    await using (var context = database.CreateContext ())
    {
      var settings = await context.DashboardSettings.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal ("Homelab", settings.Title);
      Assert.Equal (ColorMode.Dark, settings.ColorMode);
    }
  }
}

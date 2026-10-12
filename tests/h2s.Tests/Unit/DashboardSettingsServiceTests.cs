using h2s.Data;
using h2s.Models;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="DashboardSettingsService" />.
/// </summary>
public class DashboardSettingsServiceTests
{
  [Fact]
  public async Task GetSettingsAsync_CreatesDefaultsWhenMissing ()
  {
    await using var   database          = await TestDatabase.CreateAsync ();
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    await using (DashboardContext context = database.CreateContext ())
      await context.DashboardSettings.ExecuteDeleteAsync (cancellationToken);

    await using (DashboardContext context = database.CreateContext ())
    {
      DashboardSettings settings = await new DashboardSettingsService (context).GetSettingsAsync ();

      Assert.Equal (expected: 1,              actual: settings.Id);
      Assert.Equal (expected: "Dashboard",    actual: settings.Title);
      Assert.Equal (expected: "",             actual: settings.Motto);
      Assert.Equal (expected: "",             actual: settings.LocalDomains);
      Assert.Equal (expected: ColorMode.Auto, actual: settings.ColorMode);
      Assert.Equal (expected: 24,             actual: settings.UptimeKumaDefaultDuration);
    }

    await using (DashboardContext context = database.CreateContext ())
      Assert.Equal (expected: 1, actual: await context.DashboardSettings.CountAsync (cancellationToken));
  }

  [Theory]
  [InlineData (0)]
  [InlineData (-5)]
  public async Task GetSettingsAsync_NormalizesInvalidDuration (int storedDuration)
  {
    await using var   database          = await TestDatabase.CreateAsync ();
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    await using (DashboardContext context = database.CreateContext ())
    {
      await context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls:
                                                          s => s.SetProperty (propertyExpression: x => x.UptimeKumaDefaultDuration,
                                                                              valueExpression: storedDuration),
                                                          cancellationToken: cancellationToken);
    }

    await using (DashboardContext context = database.CreateContext ())
      Assert.Equal (expected: 24,
                    actual: (await new DashboardSettingsService (context).GetSettingsAsync ()).UptimeKumaDefaultDuration);

    await using (DashboardContext context = database.CreateContext ())
      Assert.Equal (expected: 24,
                    actual: (await context.DashboardSettings.SingleAsync (cancellationToken)).UptimeKumaDefaultDuration);
  }

  [Fact]
  public async Task SaveSettingsAsync_PersistsChanges ()
  {
    await using var database = await TestDatabase.CreateAsync ();

    await using (DashboardContext context = database.CreateContext ())
    {
      var               service  = new DashboardSettingsService (context);
      DashboardSettings settings = await service.GetSettingsAsync ();

      settings.Title     = "Homelab";
      settings.ColorMode = ColorMode.Dark;
      await service.SaveSettingsAsync (settings);
    }

    await using (DashboardContext context = database.CreateContext ())
    {
      DashboardSettings settings = await context.DashboardSettings.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal (expected: "Homelab",      actual: settings.Title);
      Assert.Equal (expected: ColorMode.Dark, actual: settings.ColorMode);
    }
  }
}

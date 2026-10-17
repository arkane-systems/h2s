using h2s.Data;
using h2s.Models;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="DashboardSettingsService" />.
/// </summary>
public class DashboardSettingsServiceTests
{
  [Fact]
  public async Task GetSettingsAsync_CachesAcrossRequests ()
  {
    await using var   database          = await TestDatabase.CreateAsync ();
    using var         cache             = new MemoryCache (new MemoryCacheOptions ());
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    await using (DashboardContext context = database.CreateContext ())
    {
      _ = await new DashboardSettingsService (context: context, cache: cache).GetSettingsAsync ();
    }

    // A write that bypasses the service isn't seen until the cache entry expires.
    await using (DashboardContext context = database.CreateContext ())
    {
      await context.DashboardSettings.ExecuteUpdateAsync (setPropertyCalls: s => s.SetProperty (propertyExpression: x => x.Title,
                                                                                                valueExpression: "Changed"),
                                                          cancellationToken: cancellationToken);
    }

    await using (DashboardContext context = database.CreateContext ())
    {
      Assert.Equal (expected: "Dashboard",
                    actual: (await new DashboardSettingsService (context: context, cache: cache).GetSettingsAsync ()).Title);
    }
  }

  [Fact]
  public async Task GetSettingsAsync_CreatesDefaultsWhenMissing ()
  {
    await using var   database          = await TestDatabase.CreateAsync ();
    using var         cache             = new MemoryCache (new MemoryCacheOptions ());
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    await using (DashboardContext context = database.CreateContext ())
    {
      await context.DashboardSettings.ExecuteDeleteAsync (cancellationToken);
    }

    await using (DashboardContext context = database.CreateContext ())
    {
      DashboardSettings settings = await new DashboardSettingsService (context: context, cache: cache).GetSettingsAsync ();

      Assert.Equal (expected: 1,              actual: settings.Id);
      Assert.Equal (expected: "Dashboard",    actual: settings.Title);
      Assert.Equal (expected: "",             actual: settings.Motto);
      Assert.Equal (expected: "",             actual: settings.LocalDomains);
      Assert.Equal (expected: ColorMode.Auto, actual: settings.ColorMode);
    }

    await using (DashboardContext context = database.CreateContext ())
    {
      Assert.Equal (expected: 1, actual: await context.DashboardSettings.CountAsync (cancellationToken));
    }
  }

  [Fact]
  public async Task GetSettingsAsync_ReturnsIndependentCopies ()
  {
    await using var              database = await TestDatabase.CreateAsync ();
    using var                    cache    = new MemoryCache (new MemoryCacheOptions ());
    await using DashboardContext context  = database.CreateContext ();
    var                          service  = new DashboardSettingsService (context: context, cache: cache);

    DashboardSettings first = await service.GetSettingsAsync ();
    first.Title = "Changed but not saved";

    Assert.Equal (expected: "Dashboard", actual: (await service.GetSettingsAsync ()).Title);
  }

  [Fact]
  public async Task SaveSettingsAsync_InvalidatesCache ()
  {
    await using var database = await TestDatabase.CreateAsync ();
    using var       cache    = new MemoryCache (new MemoryCacheOptions ());

    await using (DashboardContext context = database.CreateContext ())
    {
      var               service  = new DashboardSettingsService (context: context, cache: cache);
      DashboardSettings settings = await service.GetSettingsAsync ();

      settings.Title = "Homelab";
      await service.SaveSettingsAsync (settings);
    }

    await using (DashboardContext context = database.CreateContext ())
    {
      Assert.Equal (expected: "Homelab",
                    actual: (await new DashboardSettingsService (context: context, cache: cache).GetSettingsAsync ()).Title);
    }
  }

  [Fact]
  public async Task SaveSettingsAsync_PersistsChanges ()
  {
    await using var database = await TestDatabase.CreateAsync ();
    using var       cache    = new MemoryCache (new MemoryCacheOptions ());

    await using (DashboardContext context = database.CreateContext ())
    {
      var               service  = new DashboardSettingsService (context: context, cache: cache);
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

  [Fact]
  public async Task SaveSettingsAsync_SavesNewlyCreatedDefaults ()
  {
    await using var   database          = await TestDatabase.CreateAsync ();
    using var         cache             = new MemoryCache (new MemoryCacheOptions ());
    CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    await using (DashboardContext context = database.CreateContext ())
    {
      await context.DashboardSettings.ExecuteDeleteAsync (cancellationToken);
    }

    // Creating the default row and saving a copy of it through the same context must not conflict.
    await using (DashboardContext context = database.CreateContext ())
    {
      var               service  = new DashboardSettingsService (context: context, cache: cache);
      DashboardSettings settings = await service.GetSettingsAsync ();

      settings.Title = "Homelab";
      await service.SaveSettingsAsync (settings);
    }

    await using (DashboardContext context = database.CreateContext ())
    {
      Assert.Equal (expected: "Homelab", actual: (await context.DashboardSettings.SingleAsync (cancellationToken)).Title);
    }
  }

  [Fact]
  public async Task SaveSettingsAsync_WritesTheSingletonRowWhateverTheId ()
  {
    await using var database = await TestDatabase.CreateAsync ();
    using var       cache    = new MemoryCache (new MemoryCacheOptions ());

    await using (DashboardContext context = database.CreateContext ())
    {
      var               service  = new DashboardSettingsService (context: context, cache: cache);
      DashboardSettings settings = await service.GetSettingsAsync ();

      settings.Id    = 7;
      settings.Title = "Homelab";
      await service.SaveSettingsAsync (settings);
    }

    await using (DashboardContext context = database.CreateContext ())
    {
      DashboardSettings settings = await context.DashboardSettings.SingleAsync (TestContext.Current.CancellationToken);

      Assert.Equal (expected: 1,         actual: settings.Id);
      Assert.Equal (expected: "Homelab", actual: settings.Title);
    }
  }
}

using System.Text.Json;
using h2s.Data;
using h2s.Models;
using h2s.Services;
using h2s.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="DashboardBackupService" />.
/// </summary>
public class DashboardBackupServiceTests
{
  [Fact]
  public async Task ExportAsync_WritesTheConfigurationInDashboardOrder ()
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();

    DashboardBackup backup = await DashboardBackupServiceTests.CreateService (context)
                                                              .ExportAsync (TestContext.Current.CancellationToken);

    Assert.Equal (expected: DashboardBackup.FormatName,     actual: backup.Format);
    Assert.Equal (expected: DashboardBackup.CurrentVersion, actual: backup.Version);
    Assert.Equal (expected: "Development",                  actual: backup.Settings!.Title);
    Assert.Equal (expected: ["Media", "Home", "Reference", "Infrastructure", "Network"],
                  actual: backup.Categories!.Select (c => c.Name));
    Assert.Equal (expected: ["Jellyfin", "Sonarr", "Radarr", "Audiobookshelf"],
                  actual: backup.Categories![0].Links!.Select (l => l.Label));
    Assert.Equal (expected: ["Compute", "Storage", "Network"], actual: backup.InfrastructureGroups!.Select (g => g.Name));
  }

  [Fact]
  public async Task ExportThenImport_ReproducesTheConfiguration ()
  {
    await using var source = await TestDatabase.CreateAsync (seed: true);
    await using var target = await TestDatabase.CreateAsync ();
    string          json;

    await using (DashboardContext context = source.CreateContext ())
    {
      json = JsonSerializer.Serialize (value: await DashboardBackupServiceTests.CreateService (context)
                                                                               .ExportAsync (TestContext.Current.CancellationToken),
                                       options: DashboardBackup.JsonOptions);
    }

    Assert.True (condition: DashboardBackupService.TryRead (json: json,
                                                            backup: out DashboardBackup? backup,
                                                            errors: out List<string> errors),
                 userMessage: string.Join (separator: '\n', values: errors));

    await using (DashboardContext context = target.CreateContext ())
    {
      await DashboardBackupServiceTests.CreateService (context)
                                       .ImportAsync (backup: backup!, cancellationToken: TestContext.Current.CancellationToken);
    }

    await using DashboardContext sourceContext = source.CreateContext ();
    await using DashboardContext targetContext = target.CreateContext ();

    Assert.Equal (expected: await DashboardBackupServiceTests.DescribeAsync (sourceContext),
                  actual: await DashboardBackupServiceTests.DescribeAsync (targetContext));
    Assert.Equal (expected: "Development",
                  actual: (await targetContext.DashboardSettings.SingleAsync (TestContext.Current.CancellationToken)).Title);
  }

  [Fact]
  public async Task ImportAsync_ReplacesWhatWasThere ()
  {
    await using var              database = await TestDatabase.CreateAsync (seed: true);
    await using DashboardContext context  = database.CreateContext ();

    var backup = new DashboardBackup (Format: DashboardBackup.FormatName,
                                      Version: DashboardBackup.CurrentVersion,
                                      ExportedAt: null,
                                      Settings: new BackupSettings (Title: "Imported",
                                                                    Motto: null,
                                                                    LocalDomains: null,
                                                                    ColorMode: ColorMode.Dark,
                                                                    UptimeKumaServerUrl: " ",
                                                                    UptimeKumaStatusPageSlug: null,
                                                                    UptimeKumaDefaultDuration: 12),
                                      Categories:
                                      [
                                        new BackupCategory (Name: "Only",
                                                            IsAdminCategory: false,
                                                            Links:
                                                            [
                                                              new BackupLink (Label: "One",
                                                                              Url: "https://one.home.arpa/",
                                                                              Description: null,
                                                                              IconName: "One App",
                                                                              IconUrl: null,
                                                                              MonitorId: 5),
                                                            ]),
                                      ],
                                      InfrastructureGroups: []);

    await DashboardBackupServiceTests.CreateService (context)
                                     .ImportAsync (backup: backup, cancellationToken: TestContext.Current.CancellationToken);

    Link link = await context.Links.Include (l => l.Category).SingleAsync (TestContext.Current.CancellationToken);
    DashboardSettings settings =
      await context.DashboardSettings.AsNoTracking ().SingleAsync (TestContext.Current.CancellationToken);

    Assert.Equal (expected: "Only",    actual: link.Category!.Name);
    Assert.Equal (expected: "one-app", actual: link.IconName);
    Assert.Equal (expected: "",        actual: link.Description);
    Assert.Equal (expected: 5,         actual: link.MonitorId);
    Assert.Empty (await context.InfrastructureGroups.ToListAsync (TestContext.Current.CancellationToken));
    Assert.Equal (expected: "Imported",     actual: settings.Title);
    Assert.Equal (expected: ColorMode.Dark, actual: settings.ColorMode);
    Assert.Null (settings.UptimeKumaServerUrl);
    Assert.Equal (expected: 12, actual: settings.UptimeKumaDefaultDuration);
  }

  [Theory]
  [InlineData ("not json",                                         "The file isn't valid JSON for a dashboard")]
  [InlineData ("""{ "format": "something-else", "version": 1 }""", "The file isn't an h2s dashboard export.")]
  [InlineData ("""{ "format": "h2s-dashboard", "version": 2 }""",
               "The file is from a newer version of h2s (format version 2); update h2s to import it.")]
  [InlineData ("""{ "format": "h2s-dashboard", "version": 1, "categories": [] }""", "The file has no settings.")]
  public void TryRead_RejectsFilesThatArentDashboards (string json, string error)
  {
    Assert.False (DashboardBackupService.TryRead (json: json,
                                                  backup: out DashboardBackup? backup,
                                                  errors: out List<string> errors));
    Assert.Null (backup);
    Assert.StartsWith (expectedStartString: error, actualString: errors[0]);
  }

  [Fact]
  public void TryRead_ListsEachProblemWithItsPlace ()
  {
    const string Json = """
                        {
                          "format": "h2s-dashboard", "version": 1,
                          "settings": { "title": "", "colorMode": "Auto", "uptimeKumaServerUrl": "status", "uptimeKumaDefaultDuration": 0 },
                          "categories": [
                            { "name": "Media", "links": [
                              { "label": "", "url": "ftp://x" },
                              { "label": "Ok", "url": "https://ok.home.arpa/", "iconUrl": "icon.png", "monitorId": -1 }
                            ] },
                            { "name": " ", "links": [] }
                          ],
                          "infrastructureGroups": [ { "name": "", "monitorId": 0 } ]
                        }
                        """;

    Assert.False (DashboardBackupService.TryRead (json: Json, backup: out _, errors: out List<string> errors));
    Assert.Equal (expected:
                  [
                    "Settings: the title is required.",
                    "Settings: the Uptime Kuma server URL must be an HTTP or HTTPS URL.",
                    "Settings: the default uptime duration must be a positive number of hours.",
                    "Category 1 (\"Media\"), link 1: the label is required.",
                    "Category 1 (\"Media\"), link 1: the URL must be an HTTP or HTTPS URL.",
                    "Category 1 (\"Media\"), link 2: the icon URL must be an HTTP or HTTPS URL.",
                    "Category 1 (\"Media\"), link 2: the monitor ID must be a positive integer.",
                    "Category 2: the name is required.",
                    "Infrastructure group 1: the name is required.",
                    "Infrastructure group 1: the monitor ID must be a positive integer.",
                  ],
                  actual: errors);
  }

  [Fact]
  public void Summarize_CountsWhatTheFileHolds ()
  {
    var backup = new DashboardBackup (Format: DashboardBackup.FormatName,
                                      Version: 1,
                                      ExportedAt: null,
                                      Settings: new BackupSettings (Title: "Home",
                                                                    Motto: null,
                                                                    LocalDomains: null,
                                                                    ColorMode: ColorMode.Auto,
                                                                    UptimeKumaServerUrl: null,
                                                                    UptimeKumaStatusPageSlug: null,
                                                                    UptimeKumaDefaultDuration: 24),
                                      Categories:
                                      [
                                        new BackupCategory (Name: "A",
                                                            IsAdminCategory: false,
                                                            Links:
                                                            [
                                                              new BackupLink (Label: "1",
                                                                              Url: "https://a/",
                                                                              Description: null,
                                                                              IconName: null,
                                                                              IconUrl: null,
                                                                              MonitorId: null),
                                                            ]),
                                        new BackupCategory (Name: "B",
                                                            IsAdminCategory: true,
                                                            Links:
                                                            [
                                                              new BackupLink (Label: "2",
                                                                              Url: "https://b/",
                                                                              Description: null,
                                                                              IconName: null,
                                                                              IconUrl: null,
                                                                              MonitorId: null),
                                                              new BackupLink (Label: "3",
                                                                              Url: "https://c/",
                                                                              Description: null,
                                                                              IconName: null,
                                                                              IconUrl: null,
                                                                              MonitorId: null),
                                                            ]),
                                      ],
                                      InfrastructureGroups: [new BackupInfrastructureGroup (Name: "G", MonitorId: 1)]);

    Assert.Equal (expected: new BackupSummary (Title: "Home",
                                               ExportedAt: null,
                                               Categories: 2,
                                               AdminCategories: 1,
                                               Links: 3,
                                               InfrastructureGroups: 1),
                  actual: DashboardBackupService.Summarize (backup));
  }

  /// <summary>
  ///   Creates the service over a context, with its own settings cache.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <returns>The service.</returns>
  private static DashboardBackupService CreateService (DashboardContext context)
    => new (context: context,
            settingsService: new DashboardSettingsService (context: context, cache: new MemoryCache (new MemoryCacheOptions ())));

  /// <summary>
  ///   Describes a database's categories and links, in dashboard order, ignoring identifiers.
  /// </summary>
  /// <param name="context">The database context.</param>
  /// <returns>One line per category and link.</returns>
  private static async Task<List<string>> DescribeAsync (DashboardContext context)
    => (await context.Categories.Include (c => c.Links)
                     .OrderBy (c => c.IsAdminCategory)
                     .ThenBy (c => c.SortOrder)
                     .ToListAsync (TestContext.Current.CancellationToken))
      .SelectMany (c => new[] { $"{c.Name} admin={c.IsAdminCategory} order={c.SortOrder}" }
                    .Concat (c.Links.OrderBy (l => l.SortOrder)
                              .Select (l => $"  {l.SortOrder} {l.Label} {l.Url} {l.Description} {l.IconName} {l.IconUrl} {l.MonitorId}")))
      .ToList ();
}

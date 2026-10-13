using h2s.Models;
using Microsoft.EntityFrameworkCore;

namespace h2s.Data.Seeding;

/// <summary>
///   Populates a dashboard database with representative sample data for development and testing.
/// </summary>
/// <remarks>
///   The sample data exercises every rendering path on the dashboard: regular and admin categories, links with and
///   without icons and descriptions, internal and external hosts, and Uptime Kuma monitor IDs. Uptime Kuma itself is
///   left disabled so that badges do not point at a nonexistent server; set a server URL in Settings to try it.
/// </remarks>
public static class DevDataSeeder
{
  /// <summary>
  ///   The local domain used by the sample links. <c>home.arpa</c> is reserved for residential networks by RFC 8375.
  /// </summary>
  public const string LocalDomain = "home.arpa";

  /// <summary>
  ///   Seeds the sample data into a newly migrated database.
  /// </summary>
  /// <param name="context">The database context to seed. Its schema must already be migrated.</param>
  /// <param name="cancellationToken">A token used to cancel the operation.</param>
  /// <exception cref="InvalidOperationException">The database already contains categories, links, or infrastructure groups.</exception>
  public static async Task SeedAsync (DashboardContext context, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull (context);

    if (await context.Categories.AnyAsync (cancellationToken) ||
        await context.Links.AnyAsync (cancellationToken)      ||
        await context.InfrastructureGroups.AnyAsync (cancellationToken))
      throw new InvalidOperationException ("The database already contains data; reset it before seeding.");

    // The AddDashboardSettings migration inserts the singleton settings row, so update it rather than adding one.
    DashboardSettings? settings =
      await context.DashboardSettings.SingleOrDefaultAsync (predicate: s => s.Id == 1, cancellationToken: cancellationToken);

    if (settings is null)
    {
      settings = new DashboardSettings { Id = 1 };
      context.DashboardSettings.Add (settings);
    }

    DevDataSeeder.ApplySettings (settings);
    context.Categories.AddRange (DevDataSeeder.CreateCategories ());
    context.InfrastructureGroups.AddRange (DevDataSeeder.CreateInfrastructureGroups ());

    await context.SaveChangesAsync (cancellationToken);
  }

  /// <summary>
  ///   Applies the sample values to the singleton settings record.
  /// </summary>
  /// <param name="settings">The settings record to update.</param>
  private static void ApplySettings (DashboardSettings settings)
  {
    settings.Title                     = "Development";
    settings.Motto                     = "Seeded sample data";
    settings.LocalDomains              = DevDataSeeder.LocalDomain;
    settings.ColorMode                 = ColorMode.Auto;
    settings.UptimeKumaServerUrl       = null;
    settings.UptimeKumaStatusPageSlug  = null;
    settings.UptimeKumaDefaultDuration = 24;
  }

  /// <summary>
  ///   Creates the sample categories and their links.
  /// </summary>
  /// <returns>The categories, each with its links attached.</returns>
  private static IEnumerable<Category> CreateCategories ()
  {
    yield return DevDataSeeder.CreateCategory (name: "Media",
                                               isAdminCategory: false,
                                               DevDataSeeder.LocalLink (label: "Jellyfin",
                                                                        host: "jellyfin",
                                                                        description: "Movies, TV and music",
                                                                        iconName: "jellyfin",
                                                                        monitorId: 11),
                                               DevDataSeeder.LocalLink (label: "Sonarr",
                                                                        host: "sonarr",
                                                                        description: "TV series manager",
                                                                        iconName: "sonarr",
                                                                        monitorId: 12),
                                               DevDataSeeder.LocalLink (label: "Radarr",
                                                                        host: "radarr",
                                                                        description: "Movie manager",
                                                                        iconName: "radarr",
                                                                        monitorId: 13),
                                               DevDataSeeder.LocalLink (label: "Audiobookshelf",
                                                                        host: "audiobooks",
                                                                        description: "",
                                                                        iconName: "audiobookshelf"));

    yield return DevDataSeeder.CreateCategory (name: "Home",
                                               isAdminCategory: false,
                                               DevDataSeeder.LocalLink (label: "Home Assistant",
                                                                        host: "homeassistant",
                                                                        description: "Home automation",
                                                                        iconName: "home-assistant",
                                                                        monitorId: 21),
                                               DevDataSeeder.LocalLink (label: "Frigate",
                                                                        host: "frigate",
                                                                        description: "Camera NVR",
                                                                        iconName: "frigate"),
                                               DevDataSeeder.LocalLink (label: "Paperless-ngx",
                                                                        host: "paperless",
                                                                        description: "Document archive",
                                                                        iconName: "paperless-ngx"),
                                               DevDataSeeder.LocalLink (label: "Recipes",
                                                                        host: "recipes",
                                                                        description: "No icon, to exercise the fallback",
                                                                        iconName: ""));

    yield return DevDataSeeder.CreateCategory (name: "Reference",
                                               isAdminCategory: false,
                                               DevDataSeeder.ExternalLink (label: "selfh.st icons",
                                                                           url: "https://selfh.st/icons/",
                                                                           description: "Icon catalog used by h2s",
                                                                           iconName: "selfh-st"),
                                               DevDataSeeder.ExternalLink (label: "GitHub",
                                                                           url: "https://github.com/arkane-systems/h2s",
                                                                           description: "h2s source",
                                                                           iconName: "github"),
                                               DevDataSeeder.ExternalLink (label: "Docker Hub",
                                                                           url: "https://hub.docker.com/",
                                                                           description: "",
                                                                           iconName: "docker"));

    yield return DevDataSeeder.CreateCategory (name: "Infrastructure",
                                               isAdminCategory: true,
                                               DevDataSeeder.LocalLink (label: "Proxmox",
                                                                        host: "proxmox",
                                                                        description: "Hypervisor cluster",
                                                                        iconName: "proxmox",
                                                                        monitorId: 1),
                                               DevDataSeeder.LocalLink (label: "TrueNAS",
                                                                        host: "truenas",
                                                                        description: "Storage",
                                                                        iconName: "truenas-scale",
                                                                        monitorId: 2),
                                               DevDataSeeder.LocalLink (label: "Traefik",
                                                                        host: "traefik",
                                                                        description: "Ingress dashboard",
                                                                        iconName: "traefik",
                                                                        monitorId: 3),
                                               DevDataSeeder.LocalLink (label: "Uptime Kuma",
                                                                        host: "status",
                                                                        description: "Service monitoring",
                                                                        iconName: "uptime-kuma"));

    yield return DevDataSeeder.CreateCategory (name: "Network",
                                               isAdminCategory: true,
                                               DevDataSeeder.LocalLink (label: "UniFi",
                                                                        host: "unifi",
                                                                        description: "Network controller",
                                                                        iconName: "ubiquiti-unifi",
                                                                        monitorId: 4),
                                               DevDataSeeder.LocalLink (label: "Pi-hole",
                                                                        host: "pihole",
                                                                        description: "DNS filtering",
                                                                        iconName: "pi-hole"));
  }

  /// <summary>
  ///   Creates the sample infrastructure groups shown in the Uptime Kuma status bar.
  /// </summary>
  /// <returns>The infrastructure groups.</returns>
  private static IEnumerable<InfrastructureGroup> CreateInfrastructureGroups ()
  {
    yield return new InfrastructureGroup { Name = "Compute", MonitorId = 1, SortOrder = 0 };
    yield return new InfrastructureGroup { Name = "Storage", MonitorId = 2, SortOrder = 1 };
    yield return new InfrastructureGroup { Name = "Network", MonitorId = 4, SortOrder = 2 };
  }

  /// <summary>
  ///   Creates a category containing the specified links.
  /// </summary>
  /// <param name="name">The category name.</param>
  /// <param name="isAdminCategory">Whether the category belongs in the admin section.</param>
  /// <param name="links">The links in the category.</param>
  /// <returns>The category.</returns>
  private static Category CreateCategory (string name, bool isAdminCategory, params Link[] links)
    => new () { Name = name, IsAdminCategory = isAdminCategory, Links = [.. links] };

  /// <summary>
  ///   Creates a link to a host within the sample local domain.
  /// </summary>
  /// <param name="label">The link label.</param>
  /// <param name="host">The host name, without the local domain.</param>
  /// <param name="description">The link description.</param>
  /// <param name="iconName">The selfh.st icon name, or an empty string for none.</param>
  /// <param name="monitorId">The optional Uptime Kuma monitor ID.</param>
  /// <returns>The link.</returns>
  private static Link LocalLink (string label, string host, string description, string iconName, int? monitorId = null)
    => DevDataSeeder.ExternalLink (label: label,
                                   url: $"https://{host}.{DevDataSeeder.LocalDomain}/",
                                   description: description,
                                   iconName: iconName,
                                   monitorId: monitorId);

  /// <summary>
  ///   Creates a link to an arbitrary URL.
  /// </summary>
  /// <param name="label">The link label.</param>
  /// <param name="url">The link URL.</param>
  /// <param name="description">The link description.</param>
  /// <param name="iconName">The selfh.st icon name, or an empty string for none.</param>
  /// <param name="monitorId">The optional Uptime Kuma monitor ID.</param>
  /// <returns>The link.</returns>
  private static Link ExternalLink (string label, string url, string description, string iconName, int? monitorId = null)
    => new ()
       {
         Label       = label,
         Url         = url,
         Description = description,
         IconName    = iconName,
         MonitorId   = monitorId,
       };
}

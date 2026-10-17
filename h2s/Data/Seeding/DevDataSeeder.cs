using h2s.Models;
using Microsoft.EntityFrameworkCore;

namespace h2s.Data.Seeding;

/// <summary>
///   Populates a dashboard database with representative sample data for development and testing.
/// </summary>
/// <remarks>
///   The sample data exercises every rendering path on the dashboard: regular and admin categories, links with and
///   without icons and descriptions, and internal and external hosts. Uptime Kuma itself is left off (there's no
///   connection), so there are no widgets and the status bar is empty; set Kuma up in Settings to try them.
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
  /// <exception cref="InvalidOperationException">The database already contains categories or links.</exception>
  public static async Task SeedAsync (DashboardContext context, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull (context);

    if (await context.Categories.AnyAsync (cancellationToken) ||
        await context.Links.AnyAsync (cancellationToken))
    {
      throw new InvalidOperationException ("The database already contains data; reset it before seeding.");
    }

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

    await context.SaveChangesAsync (cancellationToken);
  }

  /// <summary>
  ///   Applies the sample values to the singleton settings record.
  /// </summary>
  /// <param name="settings">The settings record to update.</param>
  private static void ApplySettings (DashboardSettings settings)
  {
    settings.Title        = "Development";
    settings.Motto        = "Seeded sample data";
    settings.LocalDomains = DevDataSeeder.LocalDomain;
    settings.ColorMode    = ColorMode.Auto;
  }

  /// <summary>
  ///   Creates the sample categories and their links.
  /// </summary>
  /// <returns>
  ///   The categories, each with its links attached. Neither is in alphabetical order, so the dashboard visibly follows
  ///   <see cref="Category.SortOrder" /> and <see cref="Link.SortOrder" />.
  /// </returns>
  private static IEnumerable<Category> CreateCategories ()
  {
    yield return DevDataSeeder.CreateCategory (name: "Media",
                                               isAdminCategory: false,
                                               sortOrder: 0,
                                               DevDataSeeder.LocalLink (label: "Jellyfin",
                                                                        host: "jellyfin",
                                                                        description: "Movies, TV and music",
                                                                        iconName: "jellyfin"),
                                               DevDataSeeder.LocalLink (label: "Sonarr",
                                                                        host: "sonarr",
                                                                        description: "TV series manager",
                                                                        iconName: "sonarr"),
                                               DevDataSeeder.LocalLink (label: "Radarr",
                                                                        host: "radarr",
                                                                        description: "Movie manager",
                                                                        iconName: "radarr"),
                                               DevDataSeeder.LocalLink (label: "Audiobookshelf",
                                                                        host: "audiobooks",
                                                                        description: "",
                                                                        iconName: "audiobookshelf"));

    yield return DevDataSeeder.CreateCategory (name: "Home",
                                               isAdminCategory: false,
                                               sortOrder: 1,
                                               DevDataSeeder.LocalLink (label: "Home Assistant",
                                                                        host: "homeassistant",
                                                                        description: "Home automation",
                                                                        iconName: "home-assistant"),
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
                                               sortOrder: 2,
                                               DevDataSeeder.ExternalLink (label: "selfh.st icons",
                                                                           url: "https://selfh.st/icons/",
                                                                           description: "Icon catalog used by h2s",
                                                                           iconName: "selfh-st"),
                                               DevDataSeeder.ExternalLink (label: "GitHub",
                                                                           url: "https://github.com/arkane-systems/h2s",
                                                                           description: "h2s source, in a new tab",
                                                                           iconName: "github",
                                                                           openInNewTab: true),
                                               DevDataSeeder.ExternalLink (label: "Docker Hub",
                                                                           url: "https://hub.docker.com/",
                                                                           description: "Icon from the site's favicon",
                                                                           iconName: "",
                                                                           iconUrl: "https://hub.docker.com/favicon.ico"));

    yield return DevDataSeeder.CreateCategory (name: "Infrastructure",
                                               isAdminCategory: true,
                                               sortOrder: 0,
                                               DevDataSeeder.LocalLink (label: "Proxmox",
                                                                        host: "proxmox",
                                                                        description: "Hypervisor cluster",
                                                                        iconName: "proxmox"),
                                               DevDataSeeder.LocalLink (label: "TrueNAS",
                                                                        host: "truenas",
                                                                        description: "Storage",
                                                                        iconName: "truenas-scale"),
                                               DevDataSeeder.LocalLink (label: "Traefik",
                                                                        host: "traefik",
                                                                        description: "Ingress dashboard",
                                                                        iconName: "traefik"),
                                               DevDataSeeder.LocalLink (label: "Uptime Kuma",
                                                                        host: "status",
                                                                        description: "Service monitoring",
                                                                        iconName: "uptime-kuma"));

    yield return DevDataSeeder.CreateCategory (name: "Network",
                                               isAdminCategory: true,
                                               sortOrder: 1,
                                               DevDataSeeder.LocalLink (label: "UniFi",
                                                                        host: "unifi",
                                                                        description: "Network controller",
                                                                        iconName: "ubiquiti-unifi"),
                                               DevDataSeeder.LocalLink (label: "Pi-hole",
                                                                        host: "pihole",
                                                                        description: "DNS filtering",
                                                                        iconName: "pi-hole"));
  }

  /// <summary>
  ///   Creates a category containing the specified links.
  /// </summary>
  /// <param name="name">The category name.</param>
  /// <param name="isAdminCategory">Whether the category belongs in the admin section.</param>
  /// <param name="sortOrder">The category's position within its section.</param>
  /// <param name="links">The links in the category, in display order.</param>
  /// <returns>The category.</returns>
  private static Category CreateCategory (string name, bool isAdminCategory, int sortOrder, params Link[] links)
  {
    for (var i = 0; i < links.Length; i++)
    {
      links[i].SortOrder = i;
    }

    return new Category { Name = name, IsAdminCategory = isAdminCategory, SortOrder = sortOrder, Links = [.. links] };
  }

  /// <summary>
  ///   Creates a link to a host within the sample local domain.
  /// </summary>
  /// <param name="label">The link label.</param>
  /// <param name="host">The host name, without the local domain.</param>
  /// <param name="description">The link description.</param>
  /// <param name="iconName">The selfh.st icon name, or an empty string for none.</param>
  /// <returns>The link.</returns>
  private static Link LocalLink (string label, string host, string description, string iconName)
    => DevDataSeeder.ExternalLink (label: label,
                                   url: $"https://{host}.{DevDataSeeder.LocalDomain}/",
                                   description: description,
                                   iconName: iconName);

  /// <summary>
  ///   Creates a link to an arbitrary URL.
  /// </summary>
  /// <param name="label">The link label.</param>
  /// <param name="url">The link URL.</param>
  /// <param name="description">The link description.</param>
  /// <param name="iconName">The selfh.st icon name, or an empty string for none.</param>
  /// <param name="iconUrl">The icon image URL used when there is no icon name, or an empty string for none.</param>
  /// <param name="openInNewTab">Whether the link opens in a new browser tab.</param>
  /// <returns>The link.</returns>
  private static Link ExternalLink (string label,
                                    string url,
                                    string description,
                                    string iconName,
                                    string iconUrl      = "",
                                    bool   openInNewTab = false)
    => new ()
       {
         Label        = label,
         Url          = url,
         Description  = description,
         IconName     = iconName,
         IconUrl      = iconUrl,
         OpenInNewTab = openInNewTab,
       };
}

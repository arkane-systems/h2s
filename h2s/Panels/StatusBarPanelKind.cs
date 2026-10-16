using h2s.Data;
using h2s.Integrations;
using h2s.Integrations.UptimeKuma;
using h2s.Models;
using h2s.Pages;
using h2s.Services;
using h2s.Widgets.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace h2s.Panels;

/// <summary>
///   The status bar panel: a status and uptime badge pair from Uptime Kuma for each infrastructure group. Until it becomes
///   multi-source (Phase 5d), it reads the Uptime Kuma connection and the infrastructure groups.
/// </summary>
public sealed class StatusBarPanelKind : IPanelKind
{
  /// <summary>
  ///   The kind's key.
  /// </summary>
  public const string KindKey = "status-bar";

  /// <summary>
  ///   The key of the option that decides whether uptime badges are shown beside the status badges.
  /// </summary>
  public const string ShowUptimeOption = "showUptime";

  /// <summary>
  ///   The key of the option holding the uptime badges' window (<see cref="UptimeKumaWindows" />); blank uses the Uptime
  ///   Kuma default.
  /// </summary>
  public const string UptimeWindowOption = "uptimeWindow";

  /// <summary>
  ///   The key of the option that held the uptime period in hours, before windows replaced it. Older backups still carry
  ///   it.
  /// </summary>
  public const string LegacyUptimeHoursOption = "uptimeHours";

  /// <summary>
  ///   Builds the URL of the Uptime Kuma status page the status bar links to.
  /// </summary>
  /// <param name="serverUrl">The Uptime Kuma server URL.</param>
  /// <param name="slug">The status page slug, if one is set.</param>
  /// <returns>The status page URL, or <c>null</c> without a slug, when the status bar isn't a link.</returns>
  public static string? BuildStatusPageUrl (string serverUrl, string? slug)
    => string.IsNullOrWhiteSpace (slug) ? null : $"{serverUrl.TrimEnd ('/')}/status/{slug}";

  /// <inheritdoc />
  public string Key => StatusBarPanelKind.KindKey;

  /// <inheritdoc />
  public string DisplayName => "Status bar";

  /// <inheritdoc />
  public bool IsSingleInstance => true;

  /// <inheritdoc />
  public IReadOnlyList<SchemaOption> Options { get; } =
    [
      new (Key: StatusBarPanelKind.ShowUptimeOption, Label: "Show uptime badges", Type: OptionType.Boolean)
      {
        DefaultValue = true, HelpText = "Shows each group's uptime beside its status.",
      },
      new (Key: StatusBarPanelKind.UptimeWindowOption, Label: "Uptime period", Type: OptionType.Select)
      {
        Choices     = UptimeKumaWindows.Choices,
        Placeholder = "The Uptime Kuma default",
        HelpText    = "The period the uptime badges cover.",
      },
    ];

  /// <inheritdoc />
  public async Task<PanelContent> GetContentAsync (PanelContext context)
  {
    UptimeKumaSettings? uptimeKuma =
      await UptimeKumaSettings.LoadAsync (connections: context.Services.GetRequiredService<ConnectionService> (),
                                          cancellationToken: context.CancellationToken);

    if (uptimeKuma == null)
      return PanelContent.Empty ("Uptime Kuma isn't set up. Add its server in Settings → Integrations.");

    List<InfrastructureGroup> groups = await context.Services.GetRequiredService<DashboardContext> ()
                                                    .InfrastructureGroups
                                                    .AsNoTracking ()
                                                    .OrderBy (g => g.SortOrder)
                                                    .ThenBy (g => g.Name)
                                                    .ToListAsync (context.CancellationToken);

    if (groups.Count == 0)
      return PanelContent.Empty ("No infrastructure groups yet. Add them under Uptime Kuma in Settings → Integrations.");

    DashboardSettings settings = await context.Services.GetRequiredService<DashboardSettingsService> ().GetSettingsAsync ();

    var    badges      = new LinkTileFactory (settings: settings, uptimeKuma: uptimeKuma);
    bool   showUptime  = context.Options.GetBoolean (StatusBarPanelKind.ShowUptimeOption);
    string window      = context.Options.GetText (StatusBarPanelKind.UptimeWindowOption);
    int    uptimeHours = UptimeKumaWindows.ToHours (window.Length == 0 ? uptimeKuma.DefaultUptimeWindow : window);

    return PanelContent.Show (partialName: "_UptimeStatusBar",
                              model: new StatusBarPanelModel (StatusPageUrl:
                                                              StatusBarPanelKind
                                                               .BuildStatusPageUrl (serverUrl: uptimeKuma.ServerUrl,
                                                                                    slug: uptimeKuma.StatusPageSlug),
                                                              Items:
                                                              [
                                                                .. groups.Select (g => new StatusBarItem (Name: g.Name,
                                                                                                          StatusBadgeUrl:
                                                                                                          badges
                                                                                                           .BuildBadgeUrl (monitorId
                                                                                                                           : g
                                                                                                                            .MonitorId,
                                                                                                                           type:
                                                                                                                           "status"),
                                                                                                          UptimeBadgeUrl: showUptime
                                                                                                                            ? badges
                                                                                                                             .BuildBadgeUrl (monitorId
                                                                                                                                             : g
                                                                                                                                              .MonitorId,
                                                                                                                                             type
                                                                                                                                             : "uptime",
                                                                                                                                             duration
                                                                                                                                             : uptimeHours)
                                                                                                                            : null)),
                                                              ]));
  }
}

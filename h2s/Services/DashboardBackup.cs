using System.Text.Json;
using System.Text.Json.Serialization;

namespace h2s.Services;

/// <summary>
///   A whole dashboard configuration as exported to, and imported from, a JSON file: the settings, the categories with
///   their links, the infrastructure groups, the panels, the search engines and the connections. Order is position:
///   categories are listed in dashboard order (each section in turn), links in category order, panels in dashboard order
///   (each zone in turn), and search engines in Settings order.
/// </summary>
/// <remarks>
///   Secrets are never exported: connections' secret settings stay behind, and must be entered again after importing
///   the file somewhere else.
/// </remarks>
/// <param name="Format">Identifies the file as an h2s dashboard; always <see cref="FormatName" />.</param>
/// <param name="Version">The format version; <see cref="CurrentVersion" /> when exported.</param>
/// <param name="ExportedAt">When the file was exported.</param>
/// <param name="Settings">The dashboard settings.</param>
/// <param name="Categories">The categories, with their links.</param>
/// <param name="InfrastructureGroups">The Uptime Kuma status bar's infrastructure groups.</param>
/// <param name="Panels">
///   The panels, from format version 2. Version 1 files have none, and importing one gives the dashboard its default
///   panels.
/// </param>
/// <param name="SearchEngines">
///   The search engines, from format version 3. Earlier files have none, and importing one gives the dashboard the default
///   engines.
/// </param>
/// <param name="Connections">
///   The connections, from format version 5. Earlier files keep Uptime Kuma's settings in <see cref="Settings" />, and
///   reading one turns them into a connection.
/// </param>
public sealed record DashboardBackup (
  string?                          Format,
  int                              Version,
  DateTimeOffset?                  ExportedAt,
  BackupSettings?                  Settings,
  List<BackupCategory>?            Categories,
  List<BackupInfrastructureGroup>? InfrastructureGroups,
  List<BackupPanel>?               Panels        = null,
  List<BackupSearchEngine>?        SearchEngines = null,
  List<BackupConnection>?          Connections   = null)
{
  /// <summary>
  ///   The value of <see cref="Format" /> in every dashboard file.
  /// </summary>
  public const string FormatName = "h2s-dashboard";

  /// <summary>
  ///   The format version this build of h2s writes. It also reads every earlier version.
  /// </summary>
  public const int CurrentVersion = 7;

  /// <summary>
  ///   The first format version with panels.
  /// </summary>
  public const int PanelsVersion = 2;

  /// <summary>
  ///   The first format version with search engines.
  /// </summary>
  public const int SearchEnginesVersion = 3;

  /// <summary>
  ///   The first format version with each link's open-in-new-tab setting. Earlier files' links open in the same tab.
  /// </summary>
  public const int OpenInNewTabVersion = 4;

  /// <summary>
  ///   The first format version with connections. Earlier files keep Uptime Kuma's settings in the settings.
  /// </summary>
  public const int ConnectionsVersion = 5;

  /// <summary>
  ///   The first format version in which uptime periods are windows (<c>1d</c>, <c>30d</c>, <c>365d</c>) rather than hours:
  ///   the Uptime Kuma connection's <c>defaultUptimeWindow</c> and the status bar panel's <c>uptimeWindow</c>. Earlier files'
  ///   hours read as the shortest window that covers them.
  /// </summary>
  public const int UptimeWindowsVersion = 6;

  /// <summary>
  ///   The first format version with each link's widgets. Earlier files' links have none.
  /// </summary>
  public const int WidgetsVersion = 7;

  /// <summary>
  ///   The JSON options for dashboard files: camel-case names, enums as names, and indented so the file reads (and
  ///   diffs) well.
  /// </summary>
  public static readonly JsonSerializerOptions JsonOptions =
    new (JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter () } };
}

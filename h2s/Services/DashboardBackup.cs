using System.Text.Json;
using System.Text.Json.Serialization;

namespace h2s.Services;

/// <summary>
///   A whole dashboard configuration as exported to, and imported from, a JSON file: the settings, the categories with
///   their links, and the infrastructure groups. Order is position: categories are listed in dashboard order (each
///   section in turn) and links in category order.
/// </summary>
/// <remarks>
///   Secrets are never exported. There are none yet; Phase 5's widget configuration will be the first.
/// </remarks>
/// <param name="Format">Identifies the file as an h2s dashboard; always <see cref="FormatName" />.</param>
/// <param name="Version">The format version; <see cref="CurrentVersion" /> when exported.</param>
/// <param name="ExportedAt">When the file was exported.</param>
/// <param name="Settings">The dashboard settings.</param>
/// <param name="Categories">The categories, with their links.</param>
/// <param name="InfrastructureGroups">The Uptime Kuma status bar's infrastructure groups.</param>
public sealed record DashboardBackup (
  string?                          Format,
  int                              Version,
  DateTimeOffset?                  ExportedAt,
  BackupSettings?                  Settings,
  List<BackupCategory>?            Categories,
  List<BackupInfrastructureGroup>? InfrastructureGroups)
{
  /// <summary>
  ///   The value of <see cref="Format" /> in every dashboard file.
  /// </summary>
  public const string FormatName = "h2s-dashboard";

  /// <summary>
  ///   The format version this build of h2s writes and reads.
  /// </summary>
  public const int CurrentVersion = 1;

  /// <summary>
  ///   The JSON options for dashboard files: camel-case names, enums as names, and indented so the file reads (and
  ///   diffs) well.
  /// </summary>
  public static readonly JsonSerializerOptions JsonOptions =
    new (JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter () } };
}

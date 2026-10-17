using h2s.Widgets.Abstractions;

namespace h2s.Panels;

/// <summary>
///   A status bar: a row of status widgets from any integration, each under its label, such as an Uptime Kuma group
///   monitor's status and uptime, or a Home Assistant entity's state. A dashboard may have several, each with its own
///   title, and a bar may link to a page, such as a status page.
/// </summary>
public sealed class StatusBarPanelKind : IPanelKind
{
  /// <summary>
  ///   The kind's key.
  /// </summary>
  public const string KindKey = "status-bar";

  /// <summary>
  ///   The key of the option holding the bar's title.
  /// </summary>
  public const string TitleOption = "title";

  /// <summary>
  ///   The key of the option holding the page the bar opens when clicked.
  /// </summary>
  public const string LinkUrlOption = "linkUrl";

  /// <summary>
  ///   The key of the option that decided whether uptime badges were shown, before each item chose for itself. Older
  ///   backups still carry it.
  /// </summary>
  public const string LegacyShowUptimeOption = "showUptime";

  /// <summary>
  ///   The key of the option that held the uptime badges' window, before each item chose its own. Older backups still carry
  ///   it.
  /// </summary>
  public const string LegacyUptimeWindowOption = "uptimeWindow";

  /// <summary>
  ///   The key of the option that held the uptime period in hours, before windows replaced it. Older backups still carry
  ///   it.
  /// </summary>
  public const string LegacyUptimeHoursOption = "uptimeHours";

  /// <summary>
  ///   The most items a status bar can have.
  /// </summary>
  public const int MaxItems = 20;

  /// <summary>
  ///   Builds the URL of an Uptime Kuma status page, which older dashboards' status bars linked to.
  /// </summary>
  /// <param name="serverUrl">The Uptime Kuma server's base URL.</param>
  /// <param name="slug">The status page slug, if any.</param>
  /// <returns>The status page URL, or <c>null</c> when there's no slug.</returns>
  public static string? BuildStatusPageUrl (string serverUrl, string? slug)
    => string.IsNullOrWhiteSpace (slug) ? null : $"{serverUrl.TrimEnd ('/')}/status/{slug.Trim ()}";

  /// <inheritdoc />
  public string Key => StatusBarPanelKind.KindKey;

  /// <inheritdoc />
  public string DisplayName => "Status bar";

  /// <inheritdoc />
  public bool IsSingleInstance => false;

  /// <inheritdoc />
  public WidgetPlacements WidgetPlacement => WidgetPlacements.StatusBar;

  /// <inheritdoc />
  public IReadOnlyList<SchemaOption> Options { get; } =
    [
      new (Key: StatusBarPanelKind.TitleOption, Label: "Title", Type: OptionType.Text)
      {
        Placeholder = "Home", HelpText = "Optional. Shown at the start of the bar, to tell several bars apart.",
      },
      new (Key: StatusBarPanelKind.LinkUrlOption, Label: "Link", Type: OptionType.Url)
      {
        Placeholder = "https://status.example.com/status/main",
        HelpText    = "Optional. The page the bar opens when clicked, such as a status page.",
      },
    ];

  /// <inheritdoc />
  public Task<PanelContent> GetContentAsync (PanelContext context)
  {
    string title = context.Options.GetText (StatusBarPanelKind.TitleOption).Trim ();
    string link  = context.Options.GetText (StatusBarPanelKind.LinkUrlOption).Trim ();

    return Task.FromResult (context.Widgets.Count == 0
                              ? PanelContent.Empty ("No items yet. Add status widgets with the panel's ✎.")
                              : PanelContent.Show (partialName: "_StatusBar",
                                                   model: new StatusBarPanelModel (Title: title.Length  == 0 ? null : title,
                                                                                   LinkUrl: link.Length == 0 ? null : link,
                                                                                   Items: context.Widgets)));
  }
}

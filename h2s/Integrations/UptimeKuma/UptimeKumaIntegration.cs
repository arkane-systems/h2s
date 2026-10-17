using System.Globalization;
using h2s.Services;
using h2s.Validation;
using h2s.Widgets.Abstractions;

namespace h2s.Integrations.UptimeKuma;

/// <summary>
///   The Uptime Kuma integration: one server, whose monitors the dashboard shows on links and in the status bar. It reads
///   every monitor from Kuma's <c>/metrics</c> endpoint with a read-only API key; a status page is optional, for the status
///   bar to link to.
/// </summary>
public sealed class UptimeKumaIntegration : IIntegration
{
  /// <summary>
  ///   The integration's key.
  /// </summary>
  public const string IntegrationKey = "uptime-kuma";

  /// <summary>
  ///   The key of the option holding the server's base URL.
  /// </summary>
  public const string ServerUrlOption = "serverUrl";

  /// <summary>
  ///   The key of the secret option holding the API key that reads <c>/metrics</c>.
  /// </summary>
  public const string ApiKeyOption = "apiKey";

  /// <summary>
  ///   The key of the option holding the status page slug.
  /// </summary>
  public const string StatusPageSlugOption = "statusPageSlug";

  /// <summary>
  ///   The key of the option holding the default uptime window.
  /// </summary>
  public const string DefaultUptimeWindowOption = "defaultUptimeWindow";

  /// <summary>
  ///   The key of the option that held the default uptime period in hours, before windows replaced it. Older backups still
  ///   carry it.
  /// </summary>
  public const string LegacyDefaultDurationOption = "defaultDuration";

  /// <summary>
  ///   The name of the choice list of every monitor Kuma reports, group monitors first.
  /// </summary>
  public const string MonitorsList = "monitors";

  /// <summary>
  ///   The message for a server URL that isn't a base URL.
  /// </summary>
  public const string InvalidServerUrl =
    "Uptime Kuma server URL must be a valid HTTP or HTTPS base URL with no path, query, or fragment.";

  /// <summary>
  ///   Builds a test result.
  /// </summary>
  /// <param name="level">How the test went.</param>
  /// <param name="message">What it found.</param>
  /// <returns>The result.</returns>
  private static ConnectionTestResult Result (ConnectionTestLevel level, string message) => new (Level: level, Message: message);

  /// <summary>
  ///   Counts something in words.
  /// </summary>
  /// <param name="count">How many.</param>
  /// <param name="noun">What, in the singular.</param>
  /// <returns>Such as "1 group" or "16 monitors".</returns>
  private static string Count (int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

  /// <summary>
  ///   Offers a group's monitors as choices: each one's ID, labelled with its name and ID, under the group's name.
  /// </summary>
  /// <param name="group">The group.</param>
  /// <returns>The choices.</returns>
  private static IEnumerable<OptionChoice> ToChoices (KumaMonitorGroup group)
    => group.Monitors.Select (m => new OptionChoice (Value: m.Id.ToString (CultureInfo.InvariantCulture),
                                                     Label: $"{m.Name} (#{m.Id})") { Group = group.Name });

  /// <inheritdoc />
  public string Key => UptimeKumaIntegration.IntegrationKey;

  /// <inheritdoc />
  public string DisplayName => "Uptime Kuma";

  /// <inheritdoc />
  public string Description
    => "Monitor widgets for links' tiles, and a status bar of infrastructure groups. The status bar's badges are still " +
       "Kuma's own, so a group's monitor must also be on a published status page for its badge to show.";

  /// <inheritdoc />
  public bool AllowsMultipleConnections => false;

  /// <inheritdoc />
  public IReadOnlyList<SchemaOption> ConnectionOptions { get; } =
    [
      new (Key: UptimeKumaIntegration.ServerUrlOption, Label: "Server URL", Type: OptionType.Url)
      {
        IsRequired = true, Placeholder = "https://status.example.com",
      },
      new (Key: UptimeKumaIntegration.ApiKeyOption, Label: "API key", Type: OptionType.Secret)
      {
        IsRequired = true,
        HelpText = "Create one in Uptime Kuma under Settings → API Keys. h2s uses it only to read Kuma's /metrics, which " +
                   "lists every monitor.",
      },
      new (Key: UptimeKumaIntegration.StatusPageSlugOption, Label: "Status page slug", Type: OptionType.Text)
      {
        Pattern            = @"[A-Za-z0-9\-]+",
        PatternDescription = "may only contain letters, numbers, and hyphens",
        Placeholder        = "main",
        HelpText           = "Optional. The status bar links to this status page.",
      },
      new (Key: UptimeKumaIntegration.DefaultUptimeWindowOption, Label: "Default uptime period", Type: OptionType.Select)
      {
        IsRequired   = true,
        Choices      = UptimeKumaWindows.Choices,
        DefaultValue = UptimeKumaWindows.OneDay,
        HelpText     = "The period uptime figures cover unless they choose their own.",
      },
    ];

  /// <inheritdoc />
  public IReadOnlyList<IWidgetKind> WidgetKinds { get; } = [new KumaMonitorWidgetKind ()];

  /// <inheritdoc />
  public string? Validate (OptionValues options)
    => new HttpBaseUrlAttribute ().IsValid (options.GetText (UptimeKumaIntegration.ServerUrlOption).Trim ())
         ? null
         : UptimeKumaIntegration.InvalidServerUrl;

  /// <inheritdoc />
  /// <remarks>
  ///   It checks whether the server answers, whether the h2s server trusts its certificate, whether Kuma accepts the API key
  ///   for <c>/metrics</c>, and, when a slug is set, whether the status page exists.
  /// </remarks>
  public async Task<ConnectionTestResult> TestAsync (ConnectionContext context)
  {
    if (UptimeKumaSettings.From (context.Options) is not { } settings)
    {
      return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Danger, message: "Enter the Uptime Kuma server URL to test.");
    }

    switch (await context.Services.GetRequiredService<UrlProbe> ()
                         .CheckAsync (url: settings.ServerUrl, cancellationToken: context.CancellationToken))
    {
      case UrlReachability.Unreachable:
        return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Danger,
                                             message: $"Couldn't reach Uptime Kuma at {settings.ServerUrl}.");

      case UrlReachability.UntrustedCertificate:
        return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Warning,
                                             message:
                                             "Uptime Kuma answers, but the h2s server doesn't trust its certificate, so it can't read " +
                                             "Kuma's monitors until the CA is mounted (see the README's section on trusting a homelab " +
                                             "certificate authority).");
    }

    if (settings.ApiKey == null)
    {
      return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Warning,
                                           message: "Uptime Kuma answers. Add an API key so h2s can read its monitors.");
    }

    var client = context.Services.GetRequiredService<UptimeKumaClient> ();
    KumaMetrics metrics = await client.GetMetricsAsync (serverUrl: settings.ServerUrl,
                                                        apiKey: settings.ApiKey,
                                                        cancellationToken: context.CancellationToken);

    if (metrics.Message != null)
    {
      return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Danger,
                                           message: $"Connected to Uptime Kuma, but: {metrics.Message}");
    }

    int groups   = metrics.Monitors.Values.Count (m => m.IsGroup);
    int monitors = metrics.Monitors.Count - groups;
    var found =
      $"Connected. Uptime Kuma reports {UptimeKumaIntegration.Count (count: monitors, noun: "monitor")} and {UptimeKumaIntegration.Count (count: groups, noun: "group")}.";

    if (settings.StatusPageSlug == null)
    {
      return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Success, message: found);
    }

    KumaMonitorList page = await client.GetStatusPageMonitorsAsync (serverUrl: settings.ServerUrl,
                                                                    slug: settings.StatusPageSlug,
                                                                    cancellationToken: context.CancellationToken);

    return page.Message == null
             ? UptimeKumaIntegration.Result (level: ConnectionTestLevel.Success, message: $"{found} The status page exists.")
             : UptimeKumaIntegration.Result (level: ConnectionTestLevel.Warning,
                                             message: $"{found} But for the status page: {page.Message}");
  }

  /// <inheritdoc />
  /// <remarks>
  ///   Its one list, <see cref="MonitorsList" />, holds every monitor <c>/metrics</c> reports, as the monitor picker lists
  ///   them: group monitors under "Groups", then the rest under "Monitors", each by name with its ID.
  /// </remarks>
  public async Task<ChoiceList?> GetChoicesAsync (string listKey, ConnectionContext context)
  {
    if (listKey != UptimeKumaIntegration.MonitorsList)
    {
      return null;
    }

    if (UptimeKumaSettings.From (context.Options) is not { } settings)
    {
      return ChoiceList.Unavailable ("Uptime Kuma's server URL isn't set.");
    }

    if (settings.ApiKey == null)
    {
      return ChoiceList.Unavailable ("Add an API key to the Uptime Kuma settings to pick monitors.");
    }

    KumaMetrics metrics = await context.Services.GetRequiredService<UptimeKumaClient> ()
                                       .GetMetricsAsync (serverUrl: settings.ServerUrl,
                                                         apiKey: settings.ApiKey,
                                                         cancellationToken: context.CancellationToken);
    KumaMonitorList list = metrics.ToMonitorList ();

    return list.Message != null
             ? ChoiceList.Unavailable (list.Message)
             : new ChoiceList ([.. list.Groups.SelectMany (UptimeKumaIntegration.ToChoices)]);
  }
}

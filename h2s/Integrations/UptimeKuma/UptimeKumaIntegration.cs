using h2s.Services;
using h2s.Validation;
using h2s.Widgets.Abstractions;

namespace h2s.Integrations.UptimeKuma;

/// <summary>
///   The Uptime Kuma integration: one server, whose badges the dashboard shows on links and in the status bar, and whose
///   status page lists the monitors to pick from.
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
  ///   The key of the option holding the status page slug.
  /// </summary>
  public const string StatusPageSlugOption = "statusPageSlug";

  /// <summary>
  ///   The key of the option holding the default uptime badge period, in hours.
  /// </summary>
  public const string DefaultDurationOption = "defaultDuration";

  /// <summary>
  ///   The default uptime badge period, in hours.
  /// </summary>
  public const int DefaultDuration = 24;

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

  /// <inheritdoc />
  public string Key => UptimeKumaIntegration.IntegrationKey;

  /// <inheritdoc />
  public string DisplayName => "Uptime Kuma";

  /// <inheritdoc />
  public string Description
    => "Status badges for links, and a status bar of infrastructure groups. Monitors must be on the status page for their " +
       "badges to show.";

  /// <inheritdoc />
  public bool AllowsMultipleConnections => false;

  /// <inheritdoc />
  public IReadOnlyList<SchemaOption> ConnectionOptions { get; } =
    [
      new (Key: UptimeKumaIntegration.ServerUrlOption, Label: "Server URL", Type: OptionType.Url)
      {
        IsRequired = true, Placeholder = "https://status.example.com",
      },
      new (Key: UptimeKumaIntegration.StatusPageSlugOption, Label: "Status page slug", Type: OptionType.Text)
      {
        Pattern            = @"[A-Za-z0-9\-]+",
        PatternDescription = "may only contain letters, numbers, and hyphens",
        Placeholder        = "main",
        HelpText           = "The status bar links to this page, and the monitor picker lists its monitors.",
      },
      new (Key: UptimeKumaIntegration.DefaultDurationOption, Label: "Default uptime duration (hours)", Type: OptionType.Integer)
      {
        IsRequired = true, Minimum = 1, DefaultValue = UptimeKumaIntegration.DefaultDuration,
      },
    ];

  /// <inheritdoc />
  public string? Validate (OptionValues options)
    => new HttpBaseUrlAttribute ().IsValid (options.GetText (UptimeKumaIntegration.ServerUrlOption).Trim ())
         ? null
         : UptimeKumaIntegration.InvalidServerUrl;

  /// <inheritdoc />
  /// <remarks>
  ///   It checks whether the server answers, whether the h2s server trusts its certificate, and whether the status page
  ///   exists and lists monitors.
  /// </remarks>
  public async Task<ConnectionTestResult> TestAsync (ConnectionContext context)
  {
    if (UptimeKumaSettings.From (context.Options) is not { } settings)
      return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Danger, message: "Enter the Uptime Kuma server URL to test.");

    switch (await context.Services.GetRequiredService<UrlProbe> ()
                         .CheckAsync (url: settings.ServerUrl, cancellationToken: context.CancellationToken))
    {
      case UrlReachability.Unreachable:
        return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Danger,
                                             message: $"Couldn't reach Uptime Kuma at {settings.ServerUrl}.");

      case UrlReachability.UntrustedCertificate:
        return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Warning,
                                             message:
                                             "Uptime Kuma answers, but the h2s server doesn't trust its certificate. Badges still load " +
                                             "in browsers that trust it, but the monitor picker can't list monitors until the CA is " +
                                             "mounted (see the README's section on trusting a homelab certificate authority).");
    }

    if (settings.StatusPageSlug == null)
    {
      return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Warning,
                                           message:
                                           "Connected to Uptime Kuma. Add a status page slug to link the status bar and pick " +
                                           "monitors from the page.");
    }

    KumaMonitorList monitors = await context.Services.GetRequiredService<UptimeKumaClient> ()
                                            .GetStatusPageMonitorsAsync (serverUrl: settings.ServerUrl,
                                                                         slug: settings.StatusPageSlug,
                                                                         cancellationToken: context.CancellationToken);

    if (monitors.Message != null)
    {
      return UptimeKumaIntegration.Result (level: ConnectionTestLevel.Danger,
                                           message: $"Connected to Uptime Kuma, but: {monitors.Message}");
    }

    int count = monitors.Groups.Sum (g => g.Monitors.Count);

    return count > 0
             ? UptimeKumaIntegration.Result (level: ConnectionTestLevel.Success,
                                             message:
                                             $"Connected. The status page lists {count} monitor{(count == 1 ? "" : "s")} in " +
                                             $"{monitors.Groups.Count} group{(monitors.Groups.Count    == 1 ? "" : "s")}.")
             : UptimeKumaIntegration.Result (level: ConnectionTestLevel.Warning,
                                             message: "Connected, but the status page lists no monitors, so badges will show N/A.");
  }
}

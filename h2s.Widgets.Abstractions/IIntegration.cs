namespace h2s.Widgets.Abstractions;

/// <summary>
///   An integration with another service, such as Uptime Kuma or Home Assistant. It proposes and the core disposes: an
///   integration declares the settings its connections need, and the core renders, stores, checks and secures them, so
///   no integration ships its own settings UI. Integrations are registered as singletons, so they hold no per-request
///   state and resolve request services through <see cref="ConnectionContext.Services" />.
/// </summary>
public interface IIntegration
{
  /// <summary>
  ///   Gets the key stored with each of the integration's connections. It never changes once connections exist.
  /// </summary>
  string Key { get; }

  /// <summary>
  ///   Gets the integration's name, as Settings shows it.
  /// </summary>
  string DisplayName { get; }

  /// <summary>
  ///   Gets a sentence or two saying what the integration adds to the dashboard, shown at the top of its Settings card.
  /// </summary>
  string Description { get; }

  /// <summary>
  ///   Gets a value indicating whether the dashboard can hold more than one connection to the integration's service, such
  ///   as two Home Assistant instances.
  /// </summary>
  bool AllowsMultipleConnections { get; }

  /// <summary>
  ///   Gets the schema of a connection's settings.
  /// </summary>
  IReadOnlyList<SchemaOption> ConnectionOptions { get; }

  /// <summary>
  ///   Checks a connection's settings beyond what their schema expresses. The core has already checked each value's type,
  ///   required flag, range and pattern. It runs on every save and import, so it mustn't call the service; that's what
  ///   <see cref="TestAsync" /> is for.
  /// </summary>
  /// <param name="options">The settings.</param>
  /// <returns>What's wrong with them, as a sentence; <c>null</c> when nothing is.</returns>
  string? Validate (OptionValues options) => null;

  /// <summary>
  ///   Tries a connection's settings against the live service, without saving them, for Settings' Test button.
  /// </summary>
  /// <param name="context">The settings and the request's services.</param>
  /// <returns>What the test found.</returns>
  Task<ConnectionTestResult> TestAsync (ConnectionContext context)
    => Task.FromResult (new ConnectionTestResult (Level: ConnectionTestLevel.Warning,
                                                  Message: $"{this.DisplayName} has no connection test."));
}

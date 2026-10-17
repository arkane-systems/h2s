using h2s.Models;
using h2s.Widgets.Abstractions;

namespace h2s.Integrations;

/// <summary>
///   Reads integrations' choice lists (<see cref="IIntegration.GetChoicesAsync" />) through their first connection, for
///   the edit forms' options that name one in <see cref="SchemaOption.ChoicesFrom" />. A list that can't be read comes back
///   with a reason, never an error, so the form can fall back to typing the value.
/// </summary>
public sealed partial class ChoiceListReader
{
  private readonly ConnectionService         connections;
  private readonly IntegrationCatalog        integrations;
  private readonly ILogger<ChoiceListReader> logger;
  private readonly IServiceProvider          services;

  /// <summary>
  ///   Initializes a new instance of the <see cref="ChoiceListReader" /> class.
  /// </summary>
  /// <param name="integrations">The known integrations.</param>
  /// <param name="connections">The service holding the integrations' connections.</param>
  /// <param name="services">The request's services, passed on to the integration.</param>
  /// <param name="logger">The logger for lists that fail.</param>
  public ChoiceListReader (IntegrationCatalog        integrations,
                           ConnectionService         connections,
                           IServiceProvider          services,
                           ILogger<ChoiceListReader> logger)
  {
    this.integrations = integrations;
    this.connections  = connections;
    this.services     = services;
    this.logger       = logger;
  }

  /// <summary>
  ///   Reads one of an integration's choice lists.
  /// </summary>
  /// <param name="integrationKey">The integration's key.</param>
  /// <param name="listKey">The list's name.</param>
  /// <param name="cancellationToken">A token used to cancel the read.</param>
  /// <returns>
  ///   The list, or one with the reason it couldn't be read (including the integration having no connection);
  ///   <c>null</c> when there's no such integration, or it has no such list.
  /// </returns>
  public async Task<ChoiceList?> ReadAsync (string integrationKey, string listKey, CancellationToken cancellationToken = default)
  {
    if (this.integrations.Find (integrationKey) is not { } integration)
    {
      return null;
    }

    Connection? connection = await this.connections.FindFirstAsync (integrationKey: integrationKey,
                                                                    cancellationToken: cancellationToken);

    if ((connection == null) || this.connections.ReadOptions (connection) is not { } options)
    {
      return ChoiceList.Unavailable ($"{integration.DisplayName} isn't set up; add it in Settings → Integrations.");
    }

    return await this.TryReadAsync (integration: integration,
                                    listKey: listKey,
                                    context: new ConnectionContext (Options: options,
                                                                    Services: this.services,
                                                                    CancellationToken: cancellationToken));
  }

  /// <summary>
  ///   Asks an integration for a list, turning an exception into a list with a reason.
  /// </summary>
  /// <param name="integration">The integration.</param>
  /// <param name="listKey">The list's name.</param>
  /// <param name="context">The connection's settings.</param>
  /// <returns>The list, or <c>null</c> when the integration has no such list.</returns>
  private async Task<ChoiceList?> TryReadAsync (IIntegration integration, string listKey, ConnectionContext context)
  {
    try
    {
      return await integration.GetChoicesAsync (listKey: listKey, context: context);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !context.CancellationToken.IsCancellationRequested)
    {
      this.LogListFailed (exception: ex, integrationKey: integration.Key, listKey: listKey);

      return ChoiceList.Unavailable ("The list couldn't be read; the server log has the details.");
    }
  }

  /// <summary>
  ///   Logs a choice list an integration failed to read.
  /// </summary>
  /// <param name="exception">What went wrong.</param>
  /// <param name="integrationKey">The integration's key.</param>
  /// <param name="listKey">The list's name.</param>
  [LoggerMessage (Level = LogLevel.Warning, Message = "The {IntegrationKey} choice list {ListKey} couldn't be read.")]
  private partial void LogListFailed (Exception exception, string integrationKey, string listKey);
}

using System.Diagnostics.CodeAnalysis;
using h2s.Models;
using h2s.Options;
using h2s.Widgets.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages.Admin;

/// <summary>
///   The Settings → Integrations handlers (<c>wwwroot/js/modules/integrations.js</c>). Each integration's card is built
///   from its connection schema, and these handlers save, test and remove its connection. Secrets are never sent back:
///   a card only learns whether each one is set.
/// </summary>
/// <remarks>
///   Each integration has one connection here; an integration that allows several gets a list when the first such
///   integration arrives.
/// </remarks>
public partial class SettingsModel
{
  /// <summary>
  ///   Gets the integrations' cards, as <see cref="DescribeIntegrationAsync" /> describes them, in registration order.
  /// </summary>
  public List<object> IntegrationCards { get; private set; } = new ();

  /// <summary>
  ///   Gets the registered integrations, in order.
  /// </summary>
  public IReadOnlyList<IIntegration> Integrations => this.integrations.Integrations;

  /// <summary>
  ///   Saves an integration's connection, creating it if it doesn't exist yet.
  /// </summary>
  /// <param name="integration">The integration's key.</param>
  /// <param name="options">The settings, as <c>options[key]</c>; booleans as <c>true</c> or <c>false</c>.</param>
  /// <param name="secrets">New secrets, as <c>secrets[key]</c>; a blank one keeps what's stored.</param>
  /// <param name="clearSecrets">The keys of secrets to clear.</param>
  /// <returns>
  ///   A JSON payload describing the integration's card, a 400 problem response when a setting isn't valid, or a 404
  ///   problem response when h2s has no such integration.
  /// </returns>
  public async Task<IActionResult> OnPostSaveConnectionAsync (string?                      integration,
                                                              Dictionary<string, string?>? options,
                                                              Dictionary<string, string?>? secrets,
                                                              string[]?                    clearSecrets)
  {
    if (this.integrations.Find (integration ?? "") is not { } found)
    {
      return this.NotFoundProblem ("h2s has no such integration.");
    }

    Connection? existing = await this.connections.FindFirstAsync (integrationKey: found.Key,
                                                                  cancellationToken: this.HttpContext.RequestAborted);

    if (!this.TryReadConnection (integration: found,
                                 existing: existing,
                                 options: options,
                                 secrets: secrets,
                                 clearSecrets: clearSecrets,
                                 form: out ConnectionForm? form,
                                 problem: out IActionResult? problem))
    {
      return problem;
    }

    _ = await this.connections.SaveOnlyAsync (integration: found,
                                              optionsJson: form.Json,
                                              secrets: form.Secrets,
                                              cancellationToken: this.HttpContext.RequestAborted);

    return new JsonResult (await this.DescribeIntegrationAsync (found));
  }

  /// <summary>
  ///   Tests an integration's settings, as entered, without saving them. Secrets left blank are taken from the stored
  ///   connection.
  /// </summary>
  /// <param name="integration">The integration's key.</param>
  /// <param name="options">The settings, as <c>options[key]</c>.</param>
  /// <param name="secrets">New secrets, as <c>secrets[key]</c>.</param>
  /// <param name="clearSecrets">The keys of secrets to leave out of the test.</param>
  /// <returns>
  ///   A JSON payload with a level (<c>success</c>, <c>warning</c> or <c>danger</c>) and a message, a 400 problem response
  ///   when a setting isn't valid, or a 404 problem response when h2s has no such integration.
  /// </returns>
  public async Task<IActionResult> OnPostTestConnectionAsync (string?                      integration,
                                                              Dictionary<string, string?>? options,
                                                              Dictionary<string, string?>? secrets,
                                                              string[]?                    clearSecrets)
  {
    if (this.integrations.Find (integration ?? "") is not { } found)
    {
      return this.NotFoundProblem ("h2s has no such integration.");
    }

    Connection? existing = await this.connections.FindFirstAsync (integrationKey: found.Key,
                                                                  cancellationToken: this.HttpContext.RequestAborted);

    if (!this.TryReadConnection (integration: found,
                                 existing: existing,
                                 options: options,
                                 secrets: secrets,
                                 clearSecrets: clearSecrets,
                                 form: out ConnectionForm? form,
                                 problem: out IActionResult? problem))
    {
      return problem;
    }

    ConnectionTestResult result =
      await found.TestAsync (new ConnectionContext (Options: form.Values,
                                                    Services: this.HttpContext.RequestServices,
                                                    CancellationToken: this.HttpContext.RequestAborted));

    return new JsonResult (new { Level = result.Level.ToString ().ToLowerInvariant (), result.Message });
  }

  /// <summary>
  ///   Removes an integration's connection, which turns the integration off.
  /// </summary>
  /// <param name="integration">The integration's key.</param>
  /// <returns>
  ///   A JSON payload describing the integration's card, or a 404 problem response when h2s has no such integration.
  /// </returns>
  public async Task<IActionResult> OnPostRemoveConnectionAsync (string? integration)
  {
    if (this.integrations.Find (integration ?? "") is not { } found)
    {
      return this.NotFoundProblem ("h2s has no such integration.");
    }

    await this.connections.DeleteAllAsync (integrationKey: found.Key, cancellationToken: this.HttpContext.RequestAborted);

    return new JsonResult (await this.DescribeIntegrationAsync (found));
  }

  /// <summary>
  ///   Describes every integration's card.
  /// </summary>
  /// <returns>The cards, in registration order.</returns>
  private async Task<List<object>> DescribeIntegrationsAsync ()
  {
    var cards = new List<object> (this.integrations.Integrations.Count);

    foreach (IIntegration integration in this.integrations.Integrations)
    {
      cards.Add (await this.DescribeIntegrationAsync (integration));
    }

    return cards;
  }

  /// <summary>
  ///   Describes an integration's card: its schema and its connection's values, with each secret only as set or not.
  /// </summary>
  /// <param name="integration">The integration.</param>
  /// <returns>
  ///   An object with the integration's key, name and description, its options, whether it has a connection, how many
  ///   widgets use it, the connection's values (the defaults without one) and which secrets are set.
  /// </returns>
  private async Task<object> DescribeIntegrationAsync (IIntegration integration)
  {
    Connection? connection = await this.connections.FindFirstAsync (integrationKey: integration.Key,
                                                                    cancellationToken: this.HttpContext.RequestAborted);
    OptionValues values = (connection == null ? null : this.connections.ReadOptions (connection)) ??
                          new OptionValues (schema: integration.ConnectionOptions, json: null);

    return new
           {
             integration.Key,
             integration.DisplayName,
             integration.Description,
             Options      = OptionDescriptions.Describe (integration.ConnectionOptions),
             IsConfigured = connection != null,
             WidgetCount = connection == null
                             ? 0
                             : await this.context.Widgets.CountAsync (predicate: w => w.ConnectionId == connection.Id,
                                                                      cancellationToken: this.HttpContext.RequestAborted),
             values.Values,
             SecretsSet = integration.ConnectionOptions
                                     .Where (o => o.Type == OptionType.Secret)
                                     .ToDictionary (keySelector: o => o.Key,
                                                    elementSelector: o => values.GetSecret (o.Key) != null),
           };
  }

  /// <summary>
  ///   Checks a posted connection against its integration's schema and rules, taking secrets left blank from the stored
  ///   connection.
  /// </summary>
  /// <param name="integration">The integration.</param>
  /// <param name="existing">The integration's stored connection, if it has one.</param>
  /// <param name="options">The posted settings.</param>
  /// <param name="secrets">The posted secrets.</param>
  /// <param name="clearSecrets">The keys of secrets to clear.</param>
  /// <param name="form">The settings, when they're valid.</param>
  /// <param name="problem">A 400 problem response saying what's wrong, when they aren't.</param>
  /// <returns><c>true</c> when the settings are valid.</returns>
  private bool TryReadConnection (IIntegration                              integration,
                                  Connection?                               existing,
                                  IReadOnlyDictionary<string, string?>?     options,
                                  IReadOnlyDictionary<string, string?>?     secrets,
                                  IReadOnlyCollection<string>?              clearSecrets,
                                  [NotNullWhen (true)] out  ConnectionForm? form,
                                  [NotNullWhen (false)] out IActionResult?  problem)
  {
    form    = null;
    problem = null;

    IReadOnlyDictionary<string, string> stored = existing == null
                                                   ? new Dictionary<string, string> ()
                                                   : this.connections.ReadSecrets (existing);

    if (!OptionForm.TryParse (schema: integration.ConnectionOptions,
                              posted: options,
                              json: out string? json,
                              error: out string? error) ||
        !OptionForm.TryResolveSecrets (schema: integration.ConnectionOptions,
                                       posted: secrets,
                                       clear: clearSecrets,
                                       stored: stored,
                                       secrets: out Dictionary<string, string>? resolved,
                                       error: out error))
    {
      problem = this.BadRequestProblem (error!);

      return false;
    }

    var values = new OptionValues (schema: integration.ConnectionOptions, json: json, secrets: resolved);

    if (integration.Validate (values) is { } invalid)
    {
      problem = this.BadRequestProblem (invalid);

      return false;
    }

    form = new ConnectionForm (Json: json, Secrets: resolved, Values: values);

    return true;
  }

  /// <summary>
  ///   A posted connection, checked.
  /// </summary>
  /// <param name="Json">The settings as the connection stores them.</param>
  /// <param name="Secrets">The secrets to store.</param>
  /// <param name="Values">The settings, secrets included.</param>
  private sealed record ConnectionForm (string Json, Dictionary<string, string> Secrets, OptionValues Values);
}

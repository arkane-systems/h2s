using h2s.Widgets.Abstractions;

namespace h2s.Integrations;

/// <summary>
///   The integrations this build of h2s knows, as registered in <c>Program.cs</c>.
/// </summary>
public sealed class IntegrationCatalog
{
  private readonly Dictionary<string, IIntegration> integrationsByKey;

  /// <summary>
  ///   Initializes a new instance of the <see cref="IntegrationCatalog" /> class.
  /// </summary>
  /// <param name="integrations">The registered integrations.</param>
  /// <exception cref="ArgumentException">Two integrations share a key.</exception>
  public IntegrationCatalog (IEnumerable<IIntegration> integrations)
  {
    this.Integrations      = [.. integrations];
    this.integrationsByKey = this.Integrations.ToDictionary (keySelector: i => i.Key, comparer: StringComparer.Ordinal);
  }

  /// <summary>
  ///   Gets the integrations, in registration order.
  /// </summary>
  public IReadOnlyList<IIntegration> Integrations { get; }

  /// <summary>
  ///   Finds an integration by its key.
  /// </summary>
  /// <param name="key">The key.</param>
  /// <returns>The integration, or <c>null</c> when no integration has that key.</returns>
  public IIntegration? Find (string key) => this.integrationsByKey.GetValueOrDefault (key);
}

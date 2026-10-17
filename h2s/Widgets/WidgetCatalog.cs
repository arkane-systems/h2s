using h2s.Integrations;

namespace h2s.Widgets;

/// <summary>
///   The widget kinds the registered integrations offer. The browser names a kind as <c>integration/kind</c>, such as
///   <c>uptime-kuma/monitor</c>.
/// </summary>
public sealed class WidgetCatalog
{
  private readonly Dictionary<string, WidgetKindEntry> entriesByReference;

  /// <summary>
  ///   Initializes a new instance of the <see cref="WidgetCatalog" /> class.
  /// </summary>
  /// <param name="integrations">The registered integrations.</param>
  /// <exception cref="ArgumentException">An integration offers two kinds with one key.</exception>
  public WidgetCatalog (IntegrationCatalog integrations)
  {
    this.Kinds =
    [
      .. integrations.Integrations.SelectMany (i => i.WidgetKinds.Select (k => new WidgetKindEntry (Integration: i,
                                                                                                    Kind: k))),
    ];
    this.entriesByReference = this.Kinds.ToDictionary (keySelector: e => e.Reference, comparer: StringComparer.Ordinal);
  }

  /// <summary>
  ///   Gets every kind, in registration order.
  /// </summary>
  public IReadOnlyList<WidgetKindEntry> Kinds { get; }

  /// <summary>
  ///   Finds a kind by its reference.
  /// </summary>
  /// <param name="reference">The reference, <c>integration/kind</c>.</param>
  /// <returns>The kind, or <c>null</c> when there's no such kind.</returns>
  public WidgetKindEntry? Find (string reference) => this.entriesByReference.GetValueOrDefault (reference.Trim ());

  /// <summary>
  ///   Finds an integration's kind.
  /// </summary>
  /// <param name="integrationKey">The integration's key.</param>
  /// <param name="kindKey">The kind's key within the integration.</param>
  /// <returns>The kind, or <c>null</c> when there's no such kind.</returns>
  public WidgetKindEntry? Find (string integrationKey, string kindKey)
    => this.entriesByReference.GetValueOrDefault (WidgetKindEntry.ReferenceOf (integrationKey: integrationKey, kindKey: kindKey));
}

namespace h2s.Panels;

/// <summary>
///   The panel kinds this build of h2s knows, as registered in <c>Program.cs</c>.
/// </summary>
public sealed class PanelCatalog
{
  private readonly Dictionary<string, IPanelKind> kindsByKey;

  /// <summary>
  ///   Initializes a new instance of the <see cref="PanelCatalog" /> class.
  /// </summary>
  /// <param name="kinds">The registered kinds.</param>
  /// <exception cref="ArgumentException">Two kinds share a key.</exception>
  public PanelCatalog (IEnumerable<IPanelKind> kinds)
  {
    this.Kinds      = [.. kinds];
    this.kindsByKey = this.Kinds.ToDictionary (keySelector: k => k.Key, comparer: StringComparer.Ordinal);
  }

  /// <summary>
  ///   Gets the kinds, in registration order.
  /// </summary>
  public IReadOnlyList<IPanelKind> Kinds { get; }

  /// <summary>
  ///   Finds a kind by its key.
  /// </summary>
  /// <param name="key">The key.</param>
  /// <returns>The kind, or <c>null</c> when no kind has that key.</returns>
  public IPanelKind? Find (string key) => this.kindsByKey.GetValueOrDefault (key);
}

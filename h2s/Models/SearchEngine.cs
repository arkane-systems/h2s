using System.ComponentModel.DataAnnotations;

namespace h2s.Models;

/// <summary>
///   A web search engine that the search panel and quick launch can send queries to.
/// </summary>
public class SearchEngine
{
  /// <summary>
  ///   The longest name that can be stored.
  /// </summary>
  public const int MaxNameLength = 100;

  /// <summary>
  ///   The longest shortcut that can be stored, without its <c>!</c>.
  /// </summary>
  public const int MaxShortcutLength = 20;

  /// <summary>
  ///   Gets or sets the unique identifier for the engine.
  /// </summary>
  public int Id { get; set; }

  /// <summary>
  ///   Gets or sets the engine's name, as the search box shows it ("Search with …").
  /// </summary>
  [Required]
  [MaxLength (SearchEngine.MaxNameLength)]
  public string Name { get; set; } = "";

  /// <summary>
  ///   Gets or sets the engine's search URL, with <c>{query}</c> where the escaped search terms go.
  /// </summary>
  [Required]
  public string UrlTemplate { get; set; } = "";

  /// <summary>
  ///   Gets or sets the engine's shortcut, without its <c>!</c> (so <c>g</c> for <c>!g</c>), or an empty string for none.
  ///   Shortcuts are lowercase and unique.
  /// </summary>
  [MaxLength (SearchEngine.MaxShortcutLength)]
  public string Shortcut { get; set; } = "";

  /// <summary>
  ///   Gets or sets a value indicating whether queries without a shortcut go to this engine. One engine is the default.
  /// </summary>
  public bool IsDefault { get; set; }

  /// <summary>
  ///   Gets or sets the engine's position in Settings → Search. Engines are ordered by this value in ascending order.
  /// </summary>
  public int SortOrder { get; set; }
}

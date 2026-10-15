using System.Text.RegularExpressions;
using h2s.Models;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Services;

/// <summary>
///   Suggests selfh.st icons for links, using the icon index that the selfh.st CDN publishes.
/// </summary>
public partial class IconCatalog
{
  /// <summary>
  ///   The name of the HTTP client used to download the icon index.
  /// </summary>
  public const string ClientName = "IconCatalog";

  /// <summary>
  ///   The URL of the selfh.st icon index, a JSON array describing every available icon.
  /// </summary>
  public const string IndexUrl = "https://cdn.jsdelivr.net/gh/selfhst/icons/index.json";

  private const string IndexCacheKey = "selfhst-icon-index";

  /// <summary>
  ///   The time allowed for downloading the icon index, which is close to a megabyte.
  /// </summary>
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds (15);

  private static readonly TimeSpan IndexCacheDuration = TimeSpan.FromHours (12);

  // A failed download is retried soon, rather than leaving suggestions off for the full cache duration.
  private static readonly TimeSpan FailedIndexCacheDuration = TimeSpan.FromMinutes (1);

  private readonly IMemoryCache cache;

  private readonly IHttpClientFactory httpClientFactory;

  /// <summary>
  ///   Initializes a new instance of the <see cref="IconCatalog" /> class.
  /// </summary>
  /// <param name="httpClientFactory">The factory that provides the icon index client.</param>
  /// <param name="cache">The cache that holds the downloaded icon index.</param>
  public IconCatalog (IHttpClientFactory httpClientFactory, IMemoryCache cache)
  {
    this.httpClientFactory = httpClientFactory;
    this.cache             = cache;
  }

  /// <summary>
  ///   Suggests an icon for a link label, when an icon with a matching name exists.
  /// </summary>
  /// <param name="label">The link label.</param>
  /// <param name="cancellationToken">A token used to cancel the lookup.</param>
  /// <returns>The icon name, or <c>null</c> when there is no matching icon or the index is unavailable.</returns>
  public Task<string?> SuggestIconNameAsync (string? label, CancellationToken cancellationToken = default)
    => this.SuggestIconNameAsync (candidates: [label], cancellationToken: cancellationToken);

  /// <summary>
  ///   Suggests an icon for the first of several candidate names that matches one, such as a link's label, its page title
  ///   and its host name.
  /// </summary>
  /// <param name="candidates">The candidate names, best first. Blank candidates are skipped.</param>
  /// <param name="cancellationToken">A token used to cancel the lookup.</param>
  /// <returns>The icon name, or <c>null</c> when nothing matches or the index is unavailable.</returns>
  public async Task<string?> SuggestIconNameAsync (IEnumerable<string?> candidates, CancellationToken cancellationToken = default)
  {
    List<string> names = candidates.Where (c => !string.IsNullOrWhiteSpace (c))
                                   .Select (c => c!)
                                   .ToList ();

    if (names.Count == 0)
      return null;

    IconIndex index = await this.GetIndexAsync (cancellationToken);

    foreach (string name in names)
    {
      if (index.Find (name) is { } reference)
        return reference;
    }

    return null;
  }

  /// <summary>
  ///   Searches the icon index for the icon picker. Exact matches come first, then names and references that start with
  ///   the query, then those that contain it, then icons whose tags mention it.
  /// </summary>
  /// <param name="query">The search text.</param>
  /// <param name="limit">The most icons to return.</param>
  /// <param name="cancellationToken">A token used to cancel downloading the index.</param>
  /// <returns>The matching icons, best first; none for a blank query or when the index is unavailable.</returns>
  public async Task<IReadOnlyList<IconInfo>> SearchAsync (string? query, int limit, CancellationToken cancellationToken = default)
  {
    string compact = IconCatalog.Compact (query ?? "");

    if (compact.Length == 0)
      return [];

    IconIndex index = await this.GetIndexAsync (cancellationToken);
    string    word  = query!.Trim ();

    return index.Icons
                .Select (icon => (Icon: icon, Rank: IconCatalog.Rank (icon: icon, compactQuery: compact, query: word)))
                .Where (match => match.Rank < int.MaxValue)
                .OrderBy (match => match.Rank)
                .ThenBy (keySelector: match => match.Icon.Name, comparer: StringComparer.OrdinalIgnoreCase)
                .Take (limit)
                .Select (match => match.Icon)
                .ToList ();
  }

  /// <summary>
  ///   Gets the names worth trying as icon names for a URL's host: its first label (<c>jellyfin</c> in
  ///   <c>jellyfin.home.arpa</c>) and the label before its top-level domain (<c>github</c> in <c>github.com</c>).
  /// </summary>
  /// <param name="url">The URL.</param>
  /// <returns>The candidate names, possibly none.</returns>
  public static IEnumerable<string> GetHostCandidates (string? url)
  {
    if (!Uri.TryCreate (uriString: url, uriKind: UriKind.Absolute, result: out Uri? uri) ||
        (uri.HostNameType != UriHostNameType.Dns))
      return [];

    string[] labels = uri.Host.Split ('.');

    return labels.Length < 2 ? labels : new[] { labels[0], labels[^2] }.Distinct ();
  }

  /// <summary>
  ///   Gets the first word of each name, for a last try at matching an icon: <c>QNAP</c> in <c>QNAP Turbo NAS</c>, or
  ///   <c>Tailscale</c> in <c>Tailscale Session Recorder</c>. Single-word names and words under three characters are
  ///   skipped.
  /// </summary>
  /// <param name="names">The names.</param>
  /// <returns>The first words, in order.</returns>
  public static IEnumerable<string> GetFirstWords (IEnumerable<string?> names)
    => names.Select (n => n?.Trim ().Split (separator: ' ', count: 2, options: StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Where (words => (words.Length == 2) && (words[0].Length >= 3))
            .Select (words => words[0]);

  /// <summary>
  ///   Gets the icon index, downloading it when it is not cached.
  /// </summary>
  /// <param name="cancellationToken">A token used to cancel the download.</param>
  /// <returns>The index, which is empty when the download failed.</returns>
  private async Task<IconIndex> GetIndexAsync (CancellationToken cancellationToken)
  {
    if (this.cache.TryGetValue (key: IconCatalog.IndexCacheKey, value: out IconIndex? cached) && (cached != null))
      return cached;

    IconIndex index = await this.DownloadIndexAsync (cancellationToken);

    _ = this.cache.Set (key: IconCatalog.IndexCacheKey,
                        value: index,
                        absoluteExpirationRelativeToNow: index.IsEmpty
                                                           ? IconCatalog.FailedIndexCacheDuration
                                                           : IconCatalog.IndexCacheDuration);

    return index;
  }

  /// <summary>
  ///   Downloads and parses the icon index.
  /// </summary>
  /// <param name="cancellationToken">A token used to cancel the download.</param>
  /// <returns>The index, or an empty index when the download or parse failed.</returns>
  private async Task<IconIndex> DownloadIndexAsync (CancellationToken cancellationToken)
  {
    try
    {
      HttpClient client = this.httpClientFactory.CreateClient (IconCatalog.ClientName);
      var entries = await client.GetFromJsonAsync<List<Entry>> (requestUri: IconCatalog.IndexUrl,
                                                                cancellationToken: cancellationToken);

      return new IconIndex (entries ?? []);
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      // Treat an unreachable or malformed index as "no suggestions", but let a cancelled request propagate uncached.
      return new IconIndex ([]);
    }
  }

  /// <summary>
  ///   Ranks an icon against a search: lower is better, and <see cref="int.MaxValue" /> means no match.
  /// </summary>
  /// <param name="icon">The icon.</param>
  /// <param name="compactQuery">The query, compacted.</param>
  /// <param name="query">The query as typed, trimmed.</param>
  /// <returns>The rank.</returns>
  private static int Rank (IconInfo icon, string compactQuery, string query)
  {
    string name      = IconCatalog.Compact (icon.Name);
    string reference = IconCatalog.Compact (icon.Reference);

    if ((name == compactQuery) || (reference == compactQuery))
      return 0;

    if (name.StartsWith (value: compactQuery, comparisonType: StringComparison.Ordinal) ||
        reference.StartsWith (value: compactQuery, comparisonType: StringComparison.Ordinal))
      return 1;

    if (name.Contains (value: compactQuery, comparisonType: StringComparison.Ordinal) ||
        reference.Contains (value: compactQuery, comparisonType: StringComparison.Ordinal))
      return 2;

    return icon.Tags.Contains (value: query, comparisonType: StringComparison.OrdinalIgnoreCase) ? 3 : int.MaxValue;
  }

  /// <summary>
  ///   Reduces a name to lowercase letters and digits, so that <c>Home Assistant</c>, <c>home-assistant</c> and
  ///   <c>homeassistant</c> compare equal.
  /// </summary>
  /// <param name="name">The name.</param>
  /// <returns>The compacted name.</returns>
  private static string Compact (string name)
    => IconCatalog.NonAlphanumericPattern ().Replace (input: name.ToLowerInvariant (), replacement: "");

  [GeneratedRegex ("[^a-z0-9]+")]
  private static partial Regex NonAlphanumericPattern ();

  /// <summary>
  ///   One icon in the selfh.st index. Only the fields h2s uses are read.
  /// </summary>
  /// <param name="Name">The display name, such as <c>Home Assistant</c>.</param>
  /// <param name="Reference">The icon name used in CDN URLs, such as <c>home-assistant</c>.</param>
  /// <param name="Tags">Comma-separated tags, such as <c>Podcasts</c>.</param>
  private sealed record Entry (string? Name, string? Reference, string? Tags);

  /// <summary>
  ///   The icon index, keyed for matching candidate names against icon references and display names.
  /// </summary>
  private sealed class IconIndex
  {
    private readonly Dictionary<string, string> byCompactName = new (StringComparer.Ordinal);

    private readonly List<IconInfo> icons = [];

    private readonly HashSet<string> references = new (StringComparer.Ordinal);

    /// <summary>
    ///   Initializes a new instance of the <see cref="IconIndex" /> class.
    /// </summary>
    /// <param name="entries">The index entries.</param>
    public IconIndex (IEnumerable<Entry> entries)
    {
      foreach (Entry entry in entries)
      {
        if (string.IsNullOrWhiteSpace (entry.Reference))
          continue;

        if (!this.references.Add (entry.Reference))
          continue;

        this.icons.Add (new IconInfo (Reference: entry.Reference,
                                      Name: string.IsNullOrWhiteSpace (entry.Name) ? entry.Reference : entry.Name,
                                      Tags: entry.Tags ?? ""));

        // The first icon to claim a compacted name keeps it.
        _ = this.byCompactName.TryAdd (key: IconCatalog.Compact (entry.Reference), value: entry.Reference);

        if (!string.IsNullOrWhiteSpace (entry.Name))
          _ = this.byCompactName.TryAdd (key: IconCatalog.Compact (entry.Name), value: entry.Reference);
      }
    }

    /// <summary>
    ///   Gets every icon in the index.
    /// </summary>
    public IReadOnlyList<IconInfo> Icons => this.icons;

    /// <summary>
    ///   Gets a value indicating whether the index holds no icons.
    /// </summary>
    public bool IsEmpty => this.references.Count == 0;

    /// <summary>
    ///   Finds the icon for a name: an exact icon name first, then one whose name or reference compacts to the same.
    /// </summary>
    /// <param name="name">The name to look up.</param>
    /// <returns>The icon reference, or <c>null</c> when none matches.</returns>
    public string? Find (string name)
    {
      string normalized = Link.NormalizeIconName (name);

      if (this.references.Contains (normalized))
        return normalized;

      string compact = IconCatalog.Compact (name);

      return compact.Length > 0 ? this.byCompactName.GetValueOrDefault (compact) : null;
    }
  }
}

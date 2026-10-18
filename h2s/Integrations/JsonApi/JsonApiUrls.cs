using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace h2s.Integrations.JsonApi;

/// <summary>
///   Joins a widget's path to its connection's base URL, so that the connection's credentials only ever go to the host
///   the connection names.
/// </summary>
public static partial class JsonApiUrls
{
  /// <summary>
  ///   The message for a path that isn't relative to the base URL.
  /// </summary>
  public const string NotRelative = "The path must be relative to the connection's base URL, such as /status or status?full=1.";

  /// <summary>
  ///   The message for a path that climbs out of the base URL.
  /// </summary>
  public const string LeavesBase = "The path leaves the connection's base URL.";

  /// <summary>
  ///   Determines whether a base URL is usable: absolute HTTP or HTTPS, without credentials, a query or a fragment, and
  ///   with an optional path prefix such as <c>/api</c>.
  /// </summary>
  /// <param name="baseUrl">The base URL, already trimmed.</param>
  /// <returns><c>true</c> when it is.</returns>
  public static bool IsValidBaseUrl (string baseUrl)
    => Uri.TryCreate (uriString: baseUrl, uriKind: UriKind.Absolute, result: out Uri? uri)                    &&
       ((uri.Scheme == Uri.UriSchemeHttp) || (uri.Scheme == Uri.UriSchemeHttps))                              &&
       string.IsNullOrEmpty (uri.UserInfo)                                                                    &&
       !baseUrl.Contains (value: '?') && !baseUrl.Contains (value: '#') && !baseUrl.Contains (value: '\\');

  /// <summary>
  ///   Resolves a widget's path against a connection's base URL: a path-prefixed base keeps its prefix, so <c>/json</c>
  ///   under <c>https://host/api</c> is <c>https://host/api/json</c>.
  /// </summary>
  /// <param name="baseUrl">The connection's base URL.</param>
  /// <param name="path">The widget's path, which may carry a query.</param>
  /// <param name="url">The URL to request, when the path is acceptable.</param>
  /// <param name="error">A sentence saying what's wrong with the path, when it isn't.</param>
  /// <returns>
  ///   <c>true</c> when the path is relative, and the URL it gives stays on the base URL's scheme, host and port and under its
  ///   path prefix.
  /// </returns>
  public static bool TryResolve (string baseUrl, string path, [NotNullWhen (true)] out Uri? url, [NotNullWhen (false)] out string? error)
  {
    url  = null;
    path = path.Trim ();

    if ((path.Length == 0)                              ||
        path.StartsWith (value: "//", comparisonType: StringComparison.Ordinal) ||
        path.Any (c => char.IsControl (c) || (c is '\\' or '#' or ' ')) ||
        JsonApiUrls.SchemePattern ().IsMatch (path)     ||
        !Uri.TryCreate (uriString: baseUrl, uriKind: UriKind.Absolute, result: out Uri? root))
    {
      error = JsonApiUrls.NotRelative;

      return false;
    }

    // The path is joined to the base's own path, whatever it starts with, so it can never replace the host.
    string prefix = root.AbsolutePath.EndsWith (value: '/') ? root.AbsolutePath : $"{root.AbsolutePath}/";
    var    joined = $"{root.GetLeftPart (UriPartial.Authority)}{prefix}{path.TrimStart ('/')}";

    if (!Uri.TryCreate (uriString: joined, uriKind: UriKind.Absolute, result: out Uri? resolved) ||
        (resolved.Scheme != root.Scheme)                                                         ||
        !string.Equals (a: resolved.Host, b: root.Host, comparisonType: StringComparison.OrdinalIgnoreCase) ||
        (resolved.Port != root.Port)                                                             ||
        !string.IsNullOrEmpty (resolved.UserInfo)                                                ||
        !resolved.AbsolutePath.StartsWith (value: prefix, comparisonType: StringComparison.Ordinal))
    {
      error = JsonApiUrls.LeavesBase;

      return false;
    }

    url   = resolved;
    error = null;

    return true;
  }

  /// <summary>
  ///   Matches a path that starts with a URL scheme, such as <c>http:</c>.
  /// </summary>
  /// <returns>The pattern.</returns>
  [GeneratedRegex (@"^[A-Za-z][A-Za-z0-9+.\-]*:", RegexOptions.CultureInvariant)]
  private static partial Regex SchemePattern ();
}

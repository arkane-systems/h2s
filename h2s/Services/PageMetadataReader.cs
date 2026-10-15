using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Services;

/// <summary>
///   Reads what the link dialog can suggest from a web page: its title, the name of the app behind it, and its icon.
/// </summary>
/// <remarks>
///   <para>
///     People usually enter an app's base URL, which often isn't the page that describes the app. The reader follows HTTP
///     redirects, and then up to <see cref="MaxClientRedirects" /> client-side ones: a <c>&lt;meta http-equiv="refresh"&gt;</c>,
///     or a stub page whose script sets <c>location</c> to a string. It follows them from error pages too (Plex answers
///     its base URL with a 401 page that redirects by script), but never takes an error page's title.
///   </para>
///   <para>
///     App names come from the web app manifest and from <c>application-name</c>, <c>apple-mobile-web-app-title</c>
///     and <c>og:site_name</c> meta tags. They help when the title is generic or missing, as it often is in
///     single-page apps.
///   </para>
///   <para>
///     Requests validate certificates as usual. A host whose certificate comes from a CA the server does not trust
///     (such as a homelab CA that hasn't been mounted into the container) simply yields no metadata.
///   </para>
/// </remarks>
public partial class PageMetadataReader
{
  /// <summary>
  ///   The name of the HTTP client used to fetch pages.
  /// </summary>
  public const string ClientName = "PageMetadata";

  /// <summary>
  ///   The most client-side redirects followed from the entered URL.
  /// </summary>
  public const int MaxClientRedirects = 2;

  // The title and the tags that name the app are in the <head>, so there's no need to read a large page to the end.
  private const int MaxBytes = 256 * 1024;

  private const int MaxManifestBytes = 64 * 1024;

  private const int MaxTitleLength = 200;

  // The size a scalable (SVG or "any") icon counts as when choosing the best icon.
  private const int ScalableIconSize = 512;

  // A script redirect is followed only from a page this small: a stub that exists to redirect, rather than a real page
  // that happens to set location somewhere in its scripts.
  private const int MaxScriptRedirectPageLength = 4096;

  /// <summary>
  ///   The time allowed for fetching a page.
  /// </summary>
  public static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

  private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes (10);

  // Titles that say what state a page is in rather than what it is.
  private static readonly HashSet<string> GenericTitles = new (StringComparer.OrdinalIgnoreCase)
                                                          {
                                                            "loading",
                                                            "please wait",
                                                            "redirecting",
                                                            "login",
                                                            "log in",
                                                            "sign in",
                                                            "signin",
                                                            "unauthorized",
                                                            "forbidden",
                                                            "not found",
                                                            "error",
                                                            "index",
                                                            "home",
                                                            "welcome",
                                                            "welcome back",
                                                            "your account",
                                                            "welcome to nginx",
                                                            "untitled",
                                                            "document",
                                                            "app",
                                                            "web",
                                                            "dashboard",
                                                            "react app",
                                                            "vite app",
                                                            "ok",
                                                            "something went wrong",
                                                            "access denied",
                                                            "bad gateway",
                                                            "service unavailable",
                                                            "internal server error",
                                                          };

  private readonly IMemoryCache cache;

  private readonly IHttpClientFactory httpClientFactory;

  /// <summary>
  ///   Initializes a new instance of the <see cref="PageMetadataReader" /> class.
  /// </summary>
  /// <param name="httpClientFactory">The factory that provides the page client.</param>
  /// <param name="cache">The cache that holds recently read metadata.</param>
  public PageMetadataReader (IHttpClientFactory httpClientFactory, IMemoryCache cache)
  {
    this.httpClientFactory = httpClientFactory;
    this.cache             = cache;
  }

  /// <summary>
  ///   Reads a page's metadata, following redirects from the entered URL to the page that describes the app.
  /// </summary>
  /// <param name="url">The page URL. Only absolute HTTP and HTTPS URLs are fetched.</param>
  /// <param name="cancellationToken">A token used to cancel the requests.</param>
  /// <returns>The metadata, which is empty when nothing useful could be read.</returns>
  public async Task<PageMetadata> ReadAsync (string? url, CancellationToken cancellationToken = default)
  {
    if (!PageMetadataReader.TryGetWebUri (value: url?.Trim (), baseUri: null, uri: out Uri? uri))
      return PageMetadata.Empty;

    var key = $"page-metadata:{uri.AbsoluteUri}";

    if (this.cache.TryGetValue (key: key, value: out PageMetadata? cached) && (cached != null))
      return cached;

    PageMetadata metadata = await this.CollectAsync (uri: uri, cancellationToken: cancellationToken);

    // Only useful reads are cached, so a page that was briefly down is tried again next time.
    if (!metadata.IsEmpty)
      _ = this.cache.Set (key: key, value: metadata, absoluteExpirationRelativeToNow: PageMetadataReader.CacheDuration);

    return metadata;
  }

  /// <summary>
  ///   Extracts the title from the start of an HTML document.
  /// </summary>
  /// <param name="html">The HTML.</param>
  /// <returns>The decoded title with whitespace collapsed, or <c>null</c> when there is none.</returns>
  public static string? ParseTitle (string html)
  {
    Match match = PageMetadataReader.TitlePattern ().Match (html);

    return match.Success ? PageMetadataReader.CleanText (match.Groups["title"].Value) : null;
  }

  /// <summary>
  ///   Cleans a page title into a label: generic parts ("Login | Grocy", "Dashboard - ESPHome", "Paperless-ngx sign in")
  ///   and taglines after a colon ("Gitea: Git with a cup of tea") are dropped, as are repeated parts, keeping the
  ///   remaining parts with their separators.
  /// </summary>
  /// <param name="title">The title.</param>
  /// <returns>The label, or <c>null</c> when the whole title is generic.</returns>
  public static string? CleanTitle (string title)
  {
    if (PageMetadataReader.IsGenericTitle (title))
      return null;

    var label = new StringBuilder ();

    foreach ((string separator, string part) in PageMetadataReader.SplitTitle (title))
      _ = label.Append (label.Length > 0 ? separator : "").Append (part);

    return label.Length > 0 ? label.ToString () : null;
  }

  /// <summary>
  ///   Gets the meaningful parts of a title, the way <see cref="CleanTitle" /> keeps them, for matching against icon names.
  /// </summary>
  /// <param name="title">The title.</param>
  /// <returns>The parts, in order.</returns>
  public static IReadOnlyList<string> GetTitleParts (string? title)
    => string.IsNullOrWhiteSpace (title) ? [] : [.. PageMetadataReader.SplitTitle (title).Select (p => p.Part)];

  /// <summary>
  ///   Determines whether a title describes a page's state ("Loading…", "401 Unauthorized", "Sign in") rather than what
  ///   it is, and so is no use as a label.
  /// </summary>
  /// <param name="title">The title.</param>
  /// <returns><c>true</c> when the title is generic.</returns>
  public static bool IsGenericTitle (string title)
  {
    string core = PageMetadataReader.StatusPrefixPattern ()
                                    .Replace (input: title.Trim ().TrimEnd ('.', '…', '!', ' '), replacement: "");

    return (core.Length == 0) || PageMetadataReader.GenericTitles.Contains (core);
  }

  /// <summary>
  ///   Finds where a page sends the browser without an HTTP redirect: a <c>&lt;meta http-equiv="refresh"&gt;</c> with a
  ///   URL, or, on a small stub page, a script that sets <c>location</c> (or calls <c>location.replace</c> or
  ///   <c>location.assign</c>) with a string at the end.
  /// </summary>
  /// <param name="html">The page.</param>
  /// <param name="pageUri">The page's URL, against which a relative target is resolved.</param>
  /// <returns>The absolute HTTP or HTTPS target, or <c>null</c> when the page doesn't redirect.</returns>
  public static Uri? FindClientRedirect (string html, Uri pageUri)
  {
    foreach (Dictionary<string, string> meta in PageMetadataReader.ReadTags (html: html, tagName: "meta"))
    {
      if (!meta.TryGetValue (key: "http-equiv", value: out string? equiv)                      ||
          !equiv.Equals (value: "refresh", comparisonType: StringComparison.OrdinalIgnoreCase) ||
          !meta.TryGetValue (key: "content", value: out string? content))
        continue;

      Match refresh = PageMetadataReader.RefreshUrlPattern ().Match (content);

      if (refresh.Success &&
          PageMetadataReader.TryGetWebUri (value: refresh.Groups["url"].Value, baseUri: pageUri, uri: out Uri? target))
        return target;
    }

    if (html.Length > PageMetadataReader.MaxScriptRedirectPageLength)
      return null;

    Match script = PageMetadataReader.ScriptRedirectPattern ().Match (html);

    return script.Success &&
           PageMetadataReader.TryGetWebUri (value: script.Groups["url"].Value, baseUri: pageUri, uri: out Uri? scripted)
             ? scripted
             : null;
  }

  /// <summary>
  ///   Splits a title on its separators (" - ", " | ", " · ", " • ", " – ", " — ") and keeps the meaningful parts: each
  ///   loses a colon tagline and a sign-in phrase, and parts that are generic, about signing in, or repeated are dropped.
  /// </summary>
  /// <param name="title">The title.</param>
  /// <returns>Each kept part with the separator that preceded it in the title.</returns>
  private static List<(string Separator, string Part)> SplitTitle (string title)
  {
    string[] tokens = PageMetadataReader.TitleSeparatorPattern ().Split (title);
    var      parts  = new List<(string Separator, string Part)> ();

    // Split with a capturing group alternates parts and separators: part, separator, part, ...
    for (var i = 0; i < tokens.Length; i += 2)
    {
      string separator = i == 0 ? "" : tokens[i - 1];
      string part      = tokens[i];

      int colon = part.IndexOf (value: ": ", comparisonType: StringComparison.Ordinal);
      if (colon > 0)
        part = part[..colon];

      part = PageMetadataReader.SignInPhrasePattern ().Replace (input: part, replacement: "").Trim ();

      if ((part.Length == 0)                                      ||
          PageMetadataReader.IsGenericTitle (part)                ||
          PageMetadataReader.SignInWordsPattern ().IsMatch (part) ||
          parts.Exists (p => p.Part.Equals (value: part, comparisonType: StringComparison.OrdinalIgnoreCase)))
        continue;

      parts.Add ((separator, part));
    }

    return parts;
  }

  /// <summary>
  ///   Fetches the entered URL and any pages it redirects to on the client, gathering the title, app names and icon.
  /// </summary>
  /// <param name="uri">The entered URL.</param>
  /// <param name="cancellationToken">A token used to cancel the requests.</param>
  /// <returns>The metadata gathered.</returns>
  private async Task<PageMetadata> CollectAsync (Uri uri, CancellationToken cancellationToken)
  {
    string? title    = null;
    Uri?    iconUrl  = null;
    Uri?    lastPage = null;
    var     appNames = new List<string> ();
    var     visited  = new HashSet<Uri> ();
    Uri?    current  = uri;

    for (var hop = 0; (current != null) && (hop <= PageMetadataReader.MaxClientRedirects) && visited.Add (current); hop++)
    {
      Page? page = await this.GetPageAsync (uri: current, cancellationToken: cancellationToken);

      if (page?.Html == null)
        break;

      lastPage = page.Uri;

      // Later pages are closer to the app itself, so what they say takes precedence.
      if (page.IsSuccess                                             &&
          PageMetadataReader.ParseTitle (page.Html) is { } pageTitle &&
          PageMetadataReader.CleanTitle (pageTitle) is { } cleanTitle)
        title = cleanTitle;

      PageDetails details = await this.ReadDetailsAsync (html: page.Html, pageUri: page.Uri, cancellationToken: cancellationToken);

      appNames.InsertRange (index: 0, collection: details.AppNames);
      iconUrl = PageMetadataReader.ChooseIcon (details.Icons) ?? iconUrl;

      current = PageMetadataReader.FindClientRedirect (html: page.Html, pageUri: page.Uri);
    }

    // A site that declares no icon may still serve the conventional one.
    if ((iconUrl == null) && (lastPage != null))
    {
      var favicon = new Uri (baseUri: lastPage, relativeUri: "/favicon.ico");

      if (await this.IsImageAsync (uri: favicon, cancellationToken: cancellationToken))
        iconUrl = favicon;
    }

    return new PageMetadata (Title: title,
                             AppNames:
                             [
                               .. appNames.Where (n => !PageMetadataReader.IsGenericTitle (n))
                                          .Distinct (StringComparer.OrdinalIgnoreCase),
                             ],
                             IconUrl: iconUrl?.AbsoluteUri);
  }

  /// <summary>
  ///   Fetches a page, following HTTP redirects.
  /// </summary>
  /// <param name="uri">The page URL.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>
  ///   The page, whose HTML is <c>null</c> when it isn't HTML; or <c>null</c> when it could not be fetched at all.
  /// </returns>
  private async Task<Page?> GetPageAsync (Uri uri, CancellationToken cancellationToken)
  {
    try
    {
      HttpClient client = this.httpClientFactory.CreateClient (PageMetadataReader.ClientName);

      using var request = new HttpRequestMessage (method: HttpMethod.Get, requestUri: uri);
      request.Headers.Accept.ParseAdd ("text/html,application/xhtml+xml;q=0.9,*/*;q=0.1");

      using HttpResponseMessage response = await client.SendAsync (request: request,
                                                                   completionOption: HttpCompletionOption.ResponseHeadersRead,
                                                                   cancellationToken: cancellationToken);

      // After HTTP redirects, relative URLs on the page resolve against where the client ended up.
      Uri     finalUri  = response.RequestMessage?.RequestUri ?? uri;
      string? mediaType = response.Content.Headers.ContentType?.MediaType;
      bool    isHtml    = (mediaType == null) || mediaType.Contains ("html");

      return new Page (Uri: finalUri,
                       IsSuccess: response.IsSuccessStatusCode,
                       Html: isHtml
                               ? await PageMetadataReader.ReadPrefixAsync (content: response.Content,
                                                                           maxBytes: PageMetadataReader.MaxBytes,
                                                                           cancellationToken: cancellationToken)
                               : null);
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      // An unreachable page, a TLS failure or a timeout just means there's nothing to suggest.
      return null;
    }
  }

  /// <summary>
  ///   Reads what a page declares about its app: names from its web app manifest's <c>short_name</c> and <c>name</c>
  ///   and its <c>application-name</c>, <c>apple-mobile-web-app-title</c> and <c>og:site_name</c> meta tags; and icons
  ///   from its <c>&lt;link rel="icon"&gt;</c> and <c>apple-touch-icon</c> tags and its manifest.
  /// </summary>
  /// <param name="html">The page.</param>
  /// <param name="pageUri">The page's URL, against which the manifest and icon URLs are resolved.</param>
  /// <param name="cancellationToken">A token used to cancel the manifest request.</param>
  /// <returns>The names, best first, and the icons.</returns>
  private async Task<PageDetails> ReadDetailsAsync (string html, Uri pageUri, CancellationToken cancellationToken)
  {
    var                              details = new PageDetails (AppNames: [], Icons: []);
    List<Dictionary<string, string>> links   = [.. PageMetadataReader.ReadTags (html: html, tagName: "link")];

    string? manifestHref = links.FirstOrDefault (link => PageMetadataReader.GetRels (link).Contains ("manifest"))
                               ?.GetValueOrDefault ("href");

    if (PageMetadataReader.TryGetWebUri (value: manifestHref, baseUri: pageUri, uri: out Uri? manifestUri))
    {
      PageDetails manifest = await this.ReadManifestAsync (uri: manifestUri, cancellationToken: cancellationToken);
      details.AppNames.AddRange (manifest.AppNames);
      details.Icons.AddRange (manifest.Icons);
    }

    foreach (Dictionary<string, string> meta in PageMetadataReader.ReadTags (html: html, tagName: "meta"))
    {
      string? key = meta.GetValueOrDefault ("name") ?? meta.GetValueOrDefault ("property");

      if (key is "application-name" or "apple-mobile-web-app-title" or "og:site_name" &&
          PageMetadataReader.CleanText (meta.GetValueOrDefault ("content") ?? "") is { } name)
        details.AppNames.Add (name);
    }

    foreach (Dictionary<string, string> link in links)
    {
      HashSet<string> rels = PageMetadataReader.GetRels (link);

      // A mask-icon is a single-colour silhouette, not something to show on a tile.
      if (rels.Contains ("mask-icon") || !(rels.Contains ("icon") || rels.Contains ("apple-touch-icon")))
        continue;

      if (PageMetadataReader.TryGetWebUri (value: link.GetValueOrDefault ("href"), baseUri: pageUri, uri: out Uri? icon))
      {
        int size = PageMetadataReader.ParseIconSize (sizes: link.GetValueOrDefault ("sizes"),
                                                     type: link.GetValueOrDefault ("type"),
                                                     uri: icon,
                                                     unknownSize: rels.Contains ("apple-touch-icon") ? 180 : 32);
        details.Icons.Add (new IconCandidate (Uri: icon, Size: size));
      }
    }

    return details;
  }

  /// <summary>
  ///   Reads a web app manifest's <c>short_name</c> and <c>name</c>, and its icons other than maskable-only ones.
  /// </summary>
  /// <param name="uri">The manifest URL, against which its icon URLs are resolved.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns>The names and icons that are present, possibly none.</returns>
  private async Task<PageDetails> ReadManifestAsync (Uri uri, CancellationToken cancellationToken)
  {
    var details = new PageDetails (AppNames: [], Icons: []);

    try
    {
      HttpClient client = this.httpClientFactory.CreateClient (PageMetadataReader.ClientName);

      using var request = new HttpRequestMessage (method: HttpMethod.Get, requestUri: uri);
      request.Headers.Accept.ParseAdd ("application/manifest+json,application/json;q=0.9,*/*;q=0.1");

      using HttpResponseMessage response = await client.SendAsync (request: request,
                                                                   completionOption: HttpCompletionOption.ResponseHeadersRead,
                                                                   cancellationToken: cancellationToken);

      if (!response.IsSuccessStatusCode)
        return details;

      string json = await PageMetadataReader.ReadPrefixAsync (content: response.Content,
                                                              maxBytes: PageMetadataReader.MaxManifestBytes,
                                                              cancellationToken: cancellationToken);

      using JsonDocument manifest = JsonDocument.Parse (json);
      JsonElement        root     = manifest.RootElement;

      if (root.ValueKind != JsonValueKind.Object)
        return details;

      foreach (string property in new[] { "short_name", "name" })
      {
        if (PageMetadataReader.GetString (element: root, property: property) is { } value &&
            PageMetadataReader.CleanText (value) is { } name)
          details.AppNames.Add (name);
      }

      if (root.TryGetProperty (propertyName: "icons", value: out JsonElement icons) && (icons.ValueKind == JsonValueKind.Array))
      {
        foreach (JsonElement icon in icons.EnumerateArray ())
        {
          if (icon.ValueKind != JsonValueKind.Object)
            continue;

          string[] purposes = (PageMetadataReader.GetString (element: icon, property: "purpose") ?? "any").Split (' ');

          if (purposes.All (p => p.Equals (value: "maskable", comparisonType: StringComparison.OrdinalIgnoreCase)) ||
              !PageMetadataReader.TryGetWebUri (value: PageMetadataReader.GetString (element: icon, property: "src"),
                                                baseUri: uri,
                                                uri: out Uri? src))
            continue;

          details.Icons.Add (new IconCandidate (Uri: src,
                                                Size: PageMetadataReader.ParseIconSize (sizes:
                                                                                        PageMetadataReader.GetString (element: icon,
                                                                                                                      property:
                                                                                                                      "sizes"),
                                                                                        type: PageMetadataReader
                                                                                         .GetString (element: icon,
                                                                                                     property: "type"),
                                                                                        uri: src,
                                                                                        unknownSize: 64)));
        }
      }

      return details;
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      // A missing, protected or malformed manifest just means fewer names and icons.
      return details;
    }
  }

  /// <summary>
  ///   Determines whether a URL serves an image, or at least something that isn't an error or a web page.
  /// </summary>
  /// <param name="uri">The URL.</param>
  /// <param name="cancellationToken">A token used to cancel the request.</param>
  /// <returns><c>true</c> when the URL answers with success and content that isn't HTML.</returns>
  private async Task<bool> IsImageAsync (Uri uri, CancellationToken cancellationToken)
  {
    try
    {
      HttpClient client = this.httpClientFactory.CreateClient (PageMetadataReader.ClientName);

      using var request = new HttpRequestMessage (method: HttpMethod.Get, requestUri: uri);
      using HttpResponseMessage response = await client.SendAsync (request: request,
                                                                   completionOption: HttpCompletionOption.ResponseHeadersRead,
                                                                   cancellationToken: cancellationToken);

      // Some servers answer every path with their app's page; that isn't a favicon.
      string? mediaType = response.Content.Headers.ContentType?.MediaType;

      return response.IsSuccessStatusCode && ((mediaType == null) || !mediaType.Contains ("html"));
    }
    catch when (!cancellationToken.IsCancellationRequested)
    {
      return false;
    }
  }

  /// <summary>
  ///   Chooses the icon to suggest: the largest, preferring vector and PNG images to ICO files at equal sizes.
  /// </summary>
  /// <param name="icons">The candidates.</param>
  /// <returns>The chosen icon's URL, or <c>null</c> when there are none.</returns>
  private static Uri? ChooseIcon (IEnumerable<IconCandidate> icons)
    => icons.OrderByDescending (i => i.Size)
            .ThenBy (i => i.Uri.AbsolutePath.EndsWith (value: ".ico", comparisonType: StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault ()
           ?.Uri;

  /// <summary>
  ///   Works out an icon's size in pixels from its declared <c>sizes</c> and type.
  /// </summary>
  /// <param name="sizes">The declared sizes, such as <c>16x16 32x32</c> or <c>any</c>.</param>
  /// <param name="type">The declared media type, if any.</param>
  /// <param name="uri">The icon URL, whose extension may reveal an SVG.</param>
  /// <param name="unknownSize">The size to assume when none is declared.</param>
  /// <returns>The largest declared dimension; scalable icons count as 512.</returns>
  private static int ParseIconSize (string? sizes, string? type, Uri uri, int unknownSize)
  {
    if ((type?.Contains ("svg") ?? false)                                                             ||
        uri.AbsolutePath.EndsWith (value: ".svg", comparisonType: StringComparison.OrdinalIgnoreCase) ||
        (sizes?.Contains (value: "any", comparisonType: StringComparison.OrdinalIgnoreCase) ?? false))
      return PageMetadataReader.ScalableIconSize;

    int largest = (sizes ?? "").Split (separator: ' ', options: StringSplitOptions.RemoveEmptyEntries)
                               .Select (size => int.TryParse (s: size.Split ('x', 'X')[0], result: out int width) ? width : 0)
                               .DefaultIfEmpty (0)
                               .Max ();

    return largest > 0 ? largest : unknownSize;
  }

  /// <summary>
  ///   Gets a link tag's relationships.
  /// </summary>
  /// <param name="link">The link tag's attributes.</param>
  /// <returns>The relationships, compared case-insensitively.</returns>
  private static HashSet<string> GetRels (Dictionary<string, string> link)
    => new (collection: (link.GetValueOrDefault ("rel") ?? "").Split (separator: ' ',
                                                                      options: StringSplitOptions.RemoveEmptyEntries),
            comparer: StringComparer.OrdinalIgnoreCase);

  /// <summary>
  ///   Gets a string property of a JSON object.
  /// </summary>
  /// <param name="element">The object.</param>
  /// <param name="property">The property name.</param>
  /// <returns>The value, or <c>null</c> when it is missing or not a string.</returns>
  private static string? GetString (JsonElement element, string property)
    => element.TryGetProperty (propertyName: property, value: out JsonElement value) && (value.ValueKind == JsonValueKind.String)
         ? value.GetString ()
         : null;

  /// <summary>
  ///   Reads the attributes of every tag with the given name.
  /// </summary>
  /// <param name="html">The HTML.</param>
  /// <param name="tagName">The tag name, such as <c>meta</c>.</param>
  /// <returns>Each tag's attributes, keyed case-insensitively, with entity-decoded values.</returns>
  private static IEnumerable<Dictionary<string, string>> ReadTags (string html, string tagName)
  {
    foreach (Match tag in Regex.Matches (input: html,
                                         pattern: $@"<{tagName}\b[^>]*>",
                                         options: RegexOptions.IgnoreCase,
                                         matchTimeout: TimeSpan.FromSeconds (1)))
    {
      var attributes = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);

      foreach (Match attribute in PageMetadataReader.AttributePattern ().Matches (tag.Value))
        attributes.TryAdd (key: attribute.Groups["name"].Value, value: WebUtility.HtmlDecode (attribute.Groups["value"].Value));

      yield return attributes;
    }
  }

  /// <summary>
  ///   Resolves a string to an absolute HTTP or HTTPS URL.
  /// </summary>
  /// <param name="value">The URL, possibly relative.</param>
  /// <param name="baseUri">The URL to resolve a relative one against, or <c>null</c> to accept only absolute URLs.</param>
  /// <param name="uri">The resolved URL.</param>
  /// <returns><c>true</c> when the value resolves to an HTTP or HTTPS URL.</returns>
  private static bool TryGetWebUri (string? value, Uri? baseUri, [NotNullWhen (true)] out Uri? uri)
  {
    uri = null;

    if (string.IsNullOrWhiteSpace (value))
      return false;

    bool resolved = baseUri == null
                      ? Uri.TryCreate (uriString: value, uriKind: UriKind.Absolute,  result: out uri)
                      : Uri.TryCreate (baseUri: baseUri, relativeUri: value.Trim (), result: out uri);

    return resolved && ((uri!.Scheme == Uri.UriSchemeHttp) || (uri.Scheme == Uri.UriSchemeHttps));
  }

  /// <summary>
  ///   Decodes entities, collapses whitespace and trims text, truncating it to a label's length.
  /// </summary>
  /// <param name="text">The raw text.</param>
  /// <returns>The cleaned text, or <c>null</c> when nothing is left.</returns>
  private static string? CleanText (string text)
  {
    string clean = PageMetadataReader.WhitespacePattern ()
                                     .Replace (input: WebUtility.HtmlDecode (text), replacement: " ")
                                     .Trim ();

    if (clean.Length > PageMetadataReader.MaxTitleLength)
      clean = clean[..PageMetadataReader.MaxTitleLength].TrimEnd ();

    return clean.Length > 0 ? clean : null;
  }

  /// <summary>
  ///   Reads up to a number of bytes of a response and decodes them with its declared character set, or UTF-8.
  /// </summary>
  /// <param name="content">The response content.</param>
  /// <param name="maxBytes">The most bytes to read.</param>
  /// <param name="cancellationToken">A token used to cancel the read.</param>
  /// <returns>The decoded text.</returns>
  private static async Task<string> ReadPrefixAsync (HttpContent content, int maxBytes, CancellationToken cancellationToken)
  {
    await using Stream stream = await content.ReadAsStreamAsync (cancellationToken);

    var buffer = new byte[maxBytes];
    var length = 0;
    int read;

    while ((length < buffer.Length) &&
           ((read = await stream.ReadAsync (buffer: buffer.AsMemory (start: length), cancellationToken: cancellationToken)) > 0))
      length += read;

    return PageMetadataReader.GetEncoding (content.Headers.ContentType?.CharSet).GetString (bytes: buffer, index: 0, count: length);
  }

  /// <summary>
  ///   Gets the encoding for a declared character set.
  /// </summary>
  /// <param name="charSet">The character set from the Content-Type header, if any.</param>
  /// <returns>The encoding, or UTF-8 when the character set is missing or unknown.</returns>
  private static Encoding GetEncoding (string? charSet)
  {
    try
    {
      return string.IsNullOrWhiteSpace (charSet) ? Encoding.UTF8 : Encoding.GetEncoding (charSet.Trim ('"', ' '));
    }
    catch (ArgumentException)
    {
      return Encoding.UTF8;
    }
  }

  [GeneratedRegex (pattern: "<title[^>]*>(?<title>.*?)</title\\s*>", options: RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex TitlePattern ();

  [GeneratedRegex ("\\s+")]
  private static partial Regex WhitespacePattern ();

  // A leading HTTP status, as in "401 Unauthorized" or "Error 404 - Not Found".
  [GeneratedRegex (pattern: "^(?:error\\s*)?\\d{3}\\b\\s*[-:–]?\\s*", options: RegexOptions.IgnoreCase)]
  private static partial Regex StatusPrefixPattern ();

  [GeneratedRegex ("""(?<name>[\w:-]+)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s"'>]+))""")]
  private static partial Regex AttributePattern ();

  // The separators between a title's parts, captured so the label can keep them.
  [GeneratedRegex ("""(\s+[-|·•–—]\s+|\s*\|\s*)""")]
  private static partial Regex TitleSeparatorPattern ();

  // A sign-in phrase at either end of a part, as in "Sign in to Foo" or "Paperless-ngx sign in".
  [GeneratedRegex (pattern: """^(?:please\s+)?(?:log\s*in|sign\s*in)(?:\s*to)?\s+|\s+(?:please\s+)?(?:log\s*in|sign\s*in)$""",
                   options: RegexOptions.IgnoreCase)]
  private static partial Regex SignInPhrasePattern ();

  // A part that is about signing in, as in "Welcome Back! Please Log in".
  [GeneratedRegex (pattern: """\b(?:log\s*in|sign\s*in)(?:to)?\b""", options: RegexOptions.IgnoreCase)]
  private static partial Regex SignInWordsPattern ();

  // The URL in a refresh's content, as in "0; url=/ui/" or "5;URL='https://example.com/'".
  [GeneratedRegex (pattern: """^\s*\d*(?:\.\d+)?\s*[;,]?\s*url\s*=\s*['"]?(?<url>[^'"]+?)['"]?\s*$""",
                   options: RegexOptions.IgnoreCase)]
  private static partial Regex RefreshUrlPattern ();

  // location = ..., location.href = ..., or location.replace/assign(...), ending in a string literal.
  [GeneratedRegex (pattern:
                   """\blocation(?:\.href)?\s*(?:=(?!=)|\.(?:replace|assign)\s*\()[^;<]*?['"](?<url>[^'"]+)['"]\s*\)?\s*(?:;|<|$)""",
                   options: RegexOptions.IgnoreCase)]
  private static partial Regex ScriptRedirectPattern ();

  /// <summary>
  ///   A fetched page.
  /// </summary>
  /// <param name="Uri">The URL the page came from, after HTTP redirects.</param>
  /// <param name="IsSuccess">Whether the response had a success status.</param>
  /// <param name="Html">The start of the page, or <c>null</c> when it isn't HTML.</param>
  private sealed record Page (Uri Uri, bool IsSuccess, string? Html);

  /// <summary>
  ///   An icon a page or manifest declares.
  /// </summary>
  /// <param name="Uri">The icon URL.</param>
  /// <param name="Size">Its largest dimension in pixels, or an estimate.</param>
  private sealed record IconCandidate (Uri Uri, int Size);

  /// <summary>
  ///   What a page (or its manifest) declares about its app.
  /// </summary>
  /// <param name="AppNames">The app names, best first.</param>
  /// <param name="Icons">The icons.</param>
  private sealed record PageDetails (List<string> AppNames, List<IconCandidate> Icons);
}

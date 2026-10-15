namespace h2s.Services;

/// <summary>
///   An icon in the selfh.st index.
/// </summary>
/// <param name="Reference">The icon name used in CDN URLs and stored on links, such as <c>home-assistant</c>.</param>
/// <param name="Name">The display name, such as <c>Home Assistant</c>.</param>
/// <param name="Tags">Comma-separated tags, possibly empty.</param>
public sealed record IconInfo (string Reference, string Name, string Tags);

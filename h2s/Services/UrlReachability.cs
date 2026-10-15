namespace h2s.Services;

/// <summary>Describes the outcome of a <see cref="UrlProbe" /> check.</summary>
public enum UrlReachability
{
  /// <summary>The server responded with a success status code.</summary>
  Reachable,

  /// <summary>The server could not be contacted or returned an error.</summary>
  Unreachable,

  /// <summary>The server was reachable but its certificate chain could not be verified, e.g. due to an untrusted internal CA.</summary>
  UntrustedCertificate,
}

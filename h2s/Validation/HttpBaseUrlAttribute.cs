using System.ComponentModel.DataAnnotations;

namespace h2s.Validation;

/// <summary>
/// Validates that a string value is a valid HTTP or HTTPS base URL containing only a scheme and host
/// (and optional port), with no path beyond the root, no query string, no fragment, and no credentials.
/// </summary>
[AttributeUsage (AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class HttpBaseUrlAttribute : ValidationAttribute
{
  /// <summary>
  /// Initializes a new instance of the <see cref="HttpBaseUrlAttribute"/> class.
  /// </summary>
  public HttpBaseUrlAttribute ()
    : base ("The {0} field must be a valid HTTP or HTTPS base URL with no path, query, or fragment.")
  {
  }

  /// <summary>
  /// Determines whether the specified value is a valid HTTP or HTTPS base URL.
  /// </summary>
  /// <param name="value">The value to validate.</param>
  /// <returns><c>true</c> if the value is null, empty, or a valid HTTP/HTTPS base URL; otherwise, <c>false</c>.</returns>
  public override bool IsValid (object? value)
  {
    // Null or empty strings are valid (use [Required] for mandatory fields)
    if (value == null || (value is string str && string.IsNullOrWhiteSpace (str)))
    {
      return true;
    }

    if (value is not string urlString)
    {
      return false;
    }

    if (!Uri.TryCreate (urlString, UriKind.Absolute, out var uri))
    {
      return false;
    }

    // Must be HTTP or HTTPS
    if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
    {
      return false;
    }

    // Must not contain credentials
    if (!string.IsNullOrEmpty (uri.UserInfo))
    {
      return false;
    }

    // Must have no path beyond the root, no query string, and no fragment
    if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty (uri.Query) || !string.IsNullOrEmpty (uri.Fragment))
    {
      return false;
    }

    return true;
  }
}

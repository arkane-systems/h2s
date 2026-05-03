using System.ComponentModel.DataAnnotations;

namespace h2s.Validation;

/// <summary>
/// Validates that a string value is a properly formatted URL.
/// Note: This attribute only validates URL format. Reachability checking should be
/// performed separately in the PageModel using <see cref="UrlReachabilityChecker"/>.
/// </summary>
[AttributeUsage (AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class ReachableUrlAttribute : ValidationAttribute
{
  private readonly UrlAttribute _urlAttribute = new ();

  /// <summary>
  /// Initializes a new instance of the <see cref="ReachableUrlAttribute"/> class.
  /// </summary>
  public ReachableUrlAttribute ()
    : base ("The {0} field must be a valid URL.")
  {
  }

  /// <summary>
  /// Determines whether the specified value is a valid URL.
  /// </summary>
  /// <param name="value">The value to validate.</param>
  /// <returns><c>true</c> if the value is null, empty, or a valid URL; otherwise, <c>false</c>.</returns>
  public override bool IsValid (object? value)
  {
    // Delegate to UrlAttribute for format validation
    return _urlAttribute.IsValid (value);
  }
}

using System.ComponentModel.DataAnnotations;

namespace h2s.Validation;

/// <summary>
/// Validates that a string value can be parsed as a positive integer (greater than zero).
/// </summary>
[AttributeUsage (AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class PositiveIntegerStringAttribute : ValidationAttribute
{
  /// <summary>
  /// Initializes a new instance of the <see cref="PositiveIntegerStringAttribute"/> class.
  /// </summary>
  public PositiveIntegerStringAttribute ()
    : base ("The {0} field must be a positive integer.")
  {
  }

  /// <summary>
  /// Determines whether the specified value is a valid positive integer string.
  /// </summary>
  /// <param name="value">The value to validate.</param>
  /// <returns><c>true</c> if the value is null, empty, or a positive integer string; otherwise, <c>false</c>.</returns>
  public override bool IsValid (object? value)
  {
    // Null or empty strings are valid (use [Required] for mandatory fields)
    if (value == null || (value is string str && string.IsNullOrWhiteSpace (str)))
    {
      return true;
    }

    // Value must be a string
    if (value is not string stringValue)
    {
      return false;
    }

    // Try to parse as integer and verify it's positive
    if (int.TryParse (stringValue, out var intValue))
    {
      return intValue > 0;
    }

    return false;
  }
}

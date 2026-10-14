using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace h2s.Validation;

/// <summary>
///   Parses the ordered ID lists that the reorder handlers receive, one form value per ID.
/// </summary>
public static class OrderedIdParser
{
  /// <summary>
  ///   Parses an ordered list of entity IDs.
  /// </summary>
  /// <param name="values">The posted values, in the desired order.</param>
  /// <param name="ids">The parsed IDs in the same order, when the list is valid.</param>
  /// <param name="error">A message describing why the list is invalid, when it is.</param>
  /// <returns>
  ///   <c>true</c> when the list is non-empty and holds distinct positive integers written in plain digits; otherwise,
  ///   <c>false</c>.
  /// </returns>
  public static bool TryParse (IReadOnlyList<string>?               values,
                               [NotNullWhen (true)] out  List<int>? ids,
                               [NotNullWhen (false)] out string?    error)
  {
    ids   = null;
    error = null;

    if ((values == null) || (values.Count == 0))
    {
      error = "Ordered IDs are required.";

      return false;
    }

    var parsed = new List<int> (values.Count);

    foreach (string value in values)
    {
      if (!int.TryParse (s: value?.Trim (), style: NumberStyles.None, provider: CultureInfo.InvariantCulture, result: out int id) ||
          (id <= 0))
      {
        error = "Ordered IDs must contain valid positive integers.";

        return false;
      }

      parsed.Add (id);
    }

    if (parsed.Distinct ().Count () != parsed.Count)
    {
      error = "Ordered IDs must not repeat an ID.";

      return false;
    }

    ids = parsed;

    return true;
  }
}

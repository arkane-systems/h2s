namespace h2s.Widgets.Abstractions;

/// <summary>
///   The choices one of an integration's choice lists offers, read from its service, or why they can't be listed.
/// </summary>
/// <param name="Choices">The choices, in order; empty when they couldn't be listed.</param>
/// <param name="Message">Why the choices couldn't be listed, as a sentence; <c>null</c> when they were.</param>
public sealed record ChoiceList (IReadOnlyList<OptionChoice> Choices, string? Message = null)
{
  /// <summary>
  ///   A list whose choices couldn't be read.
  /// </summary>
  /// <param name="message">Why, as a sentence.</param>
  /// <returns>The list, with no choices.</returns>
  public static ChoiceList Unavailable (string message) => new (Choices: [], Message: message);
}

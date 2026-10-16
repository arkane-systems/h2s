namespace h2s.Widgets.Abstractions;

/// <summary>
///   One of a select option's choices.
/// </summary>
/// <param name="Value">The value stored when it's chosen.</param>
/// <param name="Label">What the edit form shows for it.</param>
public sealed record OptionChoice (string Value, string Label);

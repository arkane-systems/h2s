namespace h2s.Widgets.Abstractions;

/// <summary>
///   What a connection test found.
/// </summary>
/// <param name="Level">How it went.</param>
/// <param name="Message">What it found, in a sentence or two that say how to fix it where that's possible.</param>
public sealed record ConnectionTestResult (ConnectionTestLevel Level, string Message);

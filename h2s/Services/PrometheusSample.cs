namespace h2s.Services;

/// <summary>
///   One sample from a Prometheus text document.
/// </summary>
/// <param name="Name">The metric's name, such as <c>monitor_status</c>.</param>
/// <param name="Labels">The sample's labels.</param>
/// <param name="Value">The sample's value.</param>
public sealed record PrometheusSample (string Name, IReadOnlyDictionary<string, string> Labels, double Value);

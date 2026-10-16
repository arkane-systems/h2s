namespace h2s.Widgets.Abstractions;

/// <summary>
///   What an integration gets when it works with one of its connections.
/// </summary>
/// <param name="Options">The connection's settings, secrets included.</param>
/// <param name="Services">The request's services, from which an integration resolves what it needs.</param>
/// <param name="CancellationToken">A token used to cancel the work.</param>
public sealed record ConnectionContext (OptionValues Options, IServiceProvider Services, CancellationToken CancellationToken);

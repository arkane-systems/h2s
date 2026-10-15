namespace h2s.Panels;

/// <summary>
///   What a panel kind gets when it builds a panel's content.
/// </summary>
/// <param name="Options">The panel's option values.</param>
/// <param name="Services">The request's services, from which a kind resolves what it needs.</param>
/// <param name="CancellationToken">A token used to cancel the work.</param>
public sealed record PanelContext (PanelOptionValues Options, IServiceProvider Services, CancellationToken CancellationToken);

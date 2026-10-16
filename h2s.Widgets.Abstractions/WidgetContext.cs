namespace h2s.Widgets.Abstractions;

/// <summary>
///   What a widget kind gets when it reads its service.
/// </summary>
/// <param name="Connection">The settings of the connection the widget uses, secrets included.</param>
/// <param name="Options">The widget's own option values.</param>
/// <param name="Services">The request's services, from which a kind resolves what it needs.</param>
/// <param name="CancellationToken">A token used to cancel the work; the core also cancels a read that takes too long.</param>
public sealed record WidgetContext (
  OptionValues      Connection,
  OptionValues      Options,
  IServiceProvider  Services,
  CancellationToken CancellationToken);

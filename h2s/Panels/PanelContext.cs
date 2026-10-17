using h2s.Widgets;
using h2s.Widgets.Abstractions;

namespace h2s.Panels;

/// <summary>
///   What a panel kind gets when it builds a panel's content.
/// </summary>
/// <param name="Options">The panel's option values.</param>
/// <param name="Widgets">
///   The panel's widgets, in order, each with its result if one was read recently, for a kind that holds widgets (see
///   <see cref="IPanelKind.WidgetPlacement" />); empty otherwise. The core loads them, and the page reads the rest.
/// </param>
/// <param name="Services">The request's services, from which a kind resolves what it needs.</param>
/// <param name="CancellationToken">A token used to cancel the work.</param>
public sealed record PanelContext (
  OptionValues              Options,
  IReadOnlyList<WidgetView> Widgets,
  IServiceProvider          Services,
  CancellationToken         CancellationToken);

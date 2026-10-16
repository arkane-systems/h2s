using h2s.Widgets.Abstractions;

namespace h2s.Panels;

/// <summary>
///   A kind of dashboard panel. A kind declares its options, and the core stores, checks and edits them; the kind only
///   turns a panel's option values into content. Kinds are registered as singletons, so they hold no per-request state
///   and resolve request services through <see cref="PanelContext.Services" />.
/// </summary>
/// <remarks>
///   The built-in search and status bar panels use this interface just as an integration's panels will (Phase 5).
/// </remarks>
public interface IPanelKind
{
  /// <summary>
  ///   Gets the key stored in <see cref="Models.Panel.Kind" />. It never changes once panels of the kind exist.
  /// </summary>
  string Key { get; }

  /// <summary>
  ///   Gets the kind's name, as edit mode shows it.
  /// </summary>
  string DisplayName { get; }

  /// <summary>
  ///   Gets a value indicating whether the dashboard can hold at most one panel of the kind.
  /// </summary>
  bool IsSingleInstance { get; }

  /// <summary>
  ///   Gets the kind's option schema.
  /// </summary>
  IReadOnlyList<SchemaOption> Options { get; }

  /// <summary>
  ///   Builds a panel's content.
  /// </summary>
  /// <param name="context">The panel's option values and the request's services.</param>
  /// <returns>The content, or <see cref="PanelContent.Empty" /> when the panel has nothing to show.</returns>
  Task<PanelContent> GetContentAsync (PanelContext context);
}

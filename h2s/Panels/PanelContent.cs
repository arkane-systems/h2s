namespace h2s.Panels;

/// <summary>
///   What a panel shows: a partial and its model, or nothing, with the reason shown in its place in edit mode.
/// </summary>
public sealed record PanelContent
{
  private PanelContent () { }

  /// <summary>
  ///   Gets the name of the partial that renders the panel, or <c>null</c> when it has nothing to show.
  /// </summary>
  public string? PartialName { get; private init; }

  /// <summary>
  ///   Gets the partial's model.
  /// </summary>
  public object? Model { get; private init; }

  /// <summary>
  ///   Gets why the panel has nothing to show, when it hasn't.
  /// </summary>
  public string? EmptyMessage { get; private init; }

  /// <summary>
  ///   Gets a value indicating whether the panel has nothing to show. Empty panels are hidden outside edit mode.
  /// </summary>
  public bool IsEmpty => this.PartialName is null;

  /// <summary>
  ///   Creates content rendered by a partial.
  /// </summary>
  /// <param name="partialName">The partial's name, such as <c>_SearchPanel</c>.</param>
  /// <param name="model">The partial's model.</param>
  /// <returns>The content.</returns>
  public static PanelContent Show (string partialName, object model) => new () { PartialName = partialName, Model = model };

  /// <summary>
  ///   Creates the content of a panel with nothing to show.
  /// </summary>
  /// <param name="message">Why, in a sentence that says how to fix it where that's possible.</param>
  /// <returns>The content.</returns>
  public static PanelContent Empty (string message) => new () { EmptyMessage = message };
}

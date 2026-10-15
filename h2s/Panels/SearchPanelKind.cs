namespace h2s.Panels;

/// <summary>
///   The search box panel: a web search form.
/// </summary>
public sealed class SearchPanelKind : IPanelKind
{
  /// <summary>
  ///   The kind's key.
  /// </summary>
  public const string KindKey = "search";

  /// <summary>
  ///   The key of the option holding the search box's placeholder text.
  /// </summary>
  public const string PlaceholderOption = "placeholder";

  /// <inheritdoc />
  public string Key => SearchPanelKind.KindKey;

  /// <inheritdoc />
  public string DisplayName => "Search";

  /// <inheritdoc />
  public bool IsSingleInstance => true;

  /// <inheritdoc />
  public IReadOnlyList<PanelOption> Options { get; } =
    [
      new (Key: SearchPanelKind.PlaceholderOption, Label: "Placeholder text", Type: PanelOptionType.Text)
      {
        DefaultValue = "Search with Bing", HelpText = "Shown in the search box until you type.",
      },
    ];

  /// <inheritdoc />
  public Task<PanelContent> GetContentAsync (PanelContext context)
    => Task.FromResult (PanelContent.Show (partialName: "_SearchPanel",
                                           model: new SearchPanelModel (context.Options.GetText (SearchPanelKind
                                                                                                  .PlaceholderOption))));
}

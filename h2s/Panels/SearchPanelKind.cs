using h2s.Data;
using h2s.Models;
using h2s.Services;
using h2s.Widgets.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace h2s.Panels;

/// <summary>
///   The search box panel: a web search form that goes through the <c>/Search</c> page, so the default engine takes the
///   query unless a <c>!shortcut</c> picks another.
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
  public IReadOnlyList<SchemaOption> Options { get; } =
    [
      new (Key: SearchPanelKind.PlaceholderOption, Label: "Placeholder text", Type: OptionType.Text)
      {
        HelpText = "Shown in the search box until you type. Leave blank for “Search with” and the default engine's name.",
      },
    ];

  /// <inheritdoc />
  public async Task<PanelContent> GetContentAsync (PanelContext context)
  {
    List<SearchEngine> engines = await context.Services.GetRequiredService<DashboardContext> ()
                                              .SearchEngines
                                              .AsNoTracking ()
                                              .OrderBy (e => e.SortOrder)
                                              .ThenBy (e => e.Name)
                                              .ToListAsync (context.CancellationToken);

    if (WebSearch.GetDefault (engines) is not { } engine)
    {
      return PanelContent.Empty ("There are no search engines. Add one in Settings → Search.");
    }

    string placeholder = context.Options.GetText (SearchPanelKind.PlaceholderOption).Trim ();

    return PanelContent.Show (partialName: "_SearchPanel",
                              model: new SearchPanelModel (Placeholder: placeholder.Length > 0
                                                                          ? placeholder
                                                                          : $"Search with {engine.Name}",
                                                           EngineName: engine.Name,
                                                           Engines:
                                                           [
                                                             .. engines.Select (e => new SearchPanelEngine (Id: e.Id,
                                                                                                            Name: e.Name,
                                                                                                            Shortcut: e.Shortcut,
                                                                                                            IsDefault: e ==
                                                                                                                       engine)),
                                                           ]));
  }
}

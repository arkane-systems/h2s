using h2s.Models;
using h2s.Panels;
using h2s.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages.Admin;

/// <summary>
///   The panel handlers dashboard edit mode calls: the panel dialog's (<c>wwwroot/js/modules/panel-dialog.js</c>) and the
///   reorder handler.
/// </summary>
public partial class EditorModel
{
  /// <summary>
  ///   Lists the panel kinds for the dialog's Add panel, with their option schemas.
  /// </summary>
  /// <returns>
  ///   A JSON payload with each kind's key, name, options and whether it can be added: a single-instance kind already on
  ///   the dashboard can't.
  /// </returns>
  public async Task<IActionResult> OnGetPanelKindsAsync ()
  {
    List<string> present = await this.context.Panels
                                     .Select (p => p.Kind)
                                     .Distinct ()
                                     .ToListAsync ();

    return new JsonResult (this.panelCatalog.Kinds.Select (k => new
                                                                {
                                                                  k.Key,
                                                                  k.DisplayName,
                                                                  IsAvailable = !k.IsSingleInstance || !present.Contains (k.Key),
                                                                  Options     = EditorModel.DescribeOptions (k.Options),
                                                                }));
  }

  /// <summary>
  ///   Returns a panel as the dialog edits it.
  /// </summary>
  /// <param name="id">The panel identifier.</param>
  /// <returns>
  ///   A JSON payload with the panel's kind, its option schema and current values (none when the kind is unknown), or a
  ///   404 problem response when the panel does not exist.
  /// </returns>
  public async Task<IActionResult> OnGetPanelAsync (int id)
  {
    Panel? panel = await this.context.Panels
                             .AsNoTracking ()
                             .FirstOrDefaultAsync (p => p.Id == id);

    if (panel == null)
      return this.NotFoundProblem ("Panel not found.");

    IPanelKind? kind = this.panelCatalog.Find (panel.Kind);

    return new JsonResult (new
                           {
                             panel.Id,
                             panel.Kind,
                             DisplayName = kind?.DisplayName ?? panel.Kind,
                             IsKnown     = kind != null,
                             Zone        = panel.Zone.ToString (),
                             Options     = EditorModel.DescribeOptions (kind?.Options ?? []),
                             Values = kind == null
                                        ? new Dictionary<string, object?> ()
                                        : new PanelOptionValues (schema: kind.Options, json: panel.OptionsJson).Values,
                           });
  }

  /// <summary>
  ///   Renders a saved panel as dashboard edit mode shows it, so the page can show a new or changed panel without
  ///   reloading.
  /// </summary>
  /// <param name="id">The panel identifier.</param>
  /// <returns>The panel slot's HTML, or a 404 problem response when the panel does not exist.</returns>
  public async Task<IActionResult> OnGetPanelSlotAsync (int id)
  {
    PanelView? panel = await this.panelService.GetPanelAsync (id: id, cancellationToken: this.HttpContext.RequestAborted);

    return panel == null ? this.NotFoundProblem ("Panel not found.") : this.Partial (viewName: "_EditablePanelSlot", model: panel);
  }

  /// <summary>
  ///   Adds a panel at the end of a zone.
  /// </summary>
  /// <param name="kind">The panel's kind key.</param>
  /// <param name="zone">The zone: <c>Top</c> or <c>Bottom</c>.</param>
  /// <param name="options">The option values, keyed by option name, as the dialog posts them.</param>
  /// <returns>A JSON payload describing the new panel, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreatePanelAsync (string? kind, string? zone, Dictionary<string, string?>? options)
  {
    if (this.panelCatalog.Find ((kind ?? "").Trim ()) is not { } panelKind)
      return this.BadRequestProblem ("Choose a kind of panel to add.");

    if (!EditorModel.TryParseZone (value: zone, zone: out PanelZone target))
      return this.BadRequestProblem ("The zone must be Top or Bottom.");

    if (await this.CheckSingleInstanceAsync (panelKind) is { } taken)
      return this.BadRequestProblem (taken);

    if (!PanelOptionForm.TryParse (schema: panelKind.Options, posted: options, json: out string? json, error: out string? error))
      return this.BadRequestProblem (error);

    int last = await this.context.Panels
                         .Where (p => p.Zone == target)
                         .MaxAsync (p => (int?)p.SortOrder) ??
               -1;

    var panel = new Panel { Kind = panelKind.Key, Zone = target, SortOrder = last + 1, OptionsJson = json };

    this.context.Panels.Add (panel);
    await this.context.SaveChangesAsync ();

    return EditorModel.PanelResult (panel: panel, kind: panelKind);
  }

  /// <summary>
  ///   Changes a panel's options.
  /// </summary>
  /// <param name="id">The panel identifier.</param>
  /// <param name="options">The option values, keyed by option name, as the dialog posts them.</param>
  /// <returns>
  ///   A JSON payload describing the panel, a 400 problem response when validation fails or the panel's kind is unknown,
  ///   or a 404 problem response when the panel does not exist.
  /// </returns>
  public async Task<IActionResult> OnPostUpdatePanelAsync (int id, Dictionary<string, string?>? options)
  {
    Panel? panel = await this.context.Panels.FirstOrDefaultAsync (p => p.Id == id);

    if (panel == null)
      return this.NotFoundProblem ("Panel not found.");

    if (this.panelCatalog.Find (panel.Kind) is not { } kind)
      return this.BadRequestProblem ("This version of h2s doesn't know this panel's kind, so it can't change its options.");

    if (!PanelOptionForm.TryParse (schema: kind.Options, posted: options, json: out string? json, error: out string? error))
      return this.BadRequestProblem (error);

    panel.OptionsJson = json;
    await this.context.SaveChangesAsync ();

    return EditorModel.PanelResult (panel: panel, kind: kind);
  }

  /// <summary>
  ///   Deletes a panel and closes the gap it leaves in its zone.
  /// </summary>
  /// <param name="id">The identifier of the panel to remove.</param>
  /// <returns>
  ///   A JSON payload with the deleted panel's ID and a snapshot that <see cref="OnPostRestorePanelAsync" /> can recreate
  ///   it from, or a 404 problem response when it does not exist.
  /// </returns>
  public async Task<IActionResult> OnPostDeletePanelAsync (int id)
  {
    Panel? panel = await this.context.Panels.FirstOrDefaultAsync (p => p.Id == id);

    if (panel == null)
      return this.NotFoundProblem ("Panel not found.");

    List<Panel> zone = await this.context.Panels
                                 .Where (p => p.Zone == panel.Zone)
                                 .OrderBy (p => p.SortOrder)
                                 .ThenBy (p => p.Id)
                                 .ToListAsync ();

    PanelSnapshot snapshot = PanelSnapshot.From (panel: panel, position: zone.IndexOf (panel));

    this.context.Panels.Remove (panel);
    _ = zone.Remove (panel);

    for (var i = 0; i < zone.Count; i++)
      zone[i].SortOrder = i;

    await this.context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id, Restore = snapshot });
  }

  /// <summary>
  ///   Recreates a deleted panel from the snapshot its delete returned, at its old position in its zone.
  /// </summary>
  /// <param name="snapshot">The <see cref="PanelSnapshot" />, as JSON.</param>
  /// <returns>
  ///   A JSON payload describing the recreated panel, or a 400 problem response when the snapshot is invalid or its kind
  ///   can't be added any more.
  /// </returns>
  public async Task<IActionResult> OnPostRestorePanelAsync (string? snapshot)
  {
    var restore = EditorModel.ReadSnapshot<PanelSnapshot> (snapshot);

    if ((restore == null) || !Enum.IsDefined (restore.Zone))
      return this.BadRequestProblem ("The panel to restore is missing or malformed.");

    if (this.panelCatalog.Find ((restore.Kind ?? "").Trim ()) is not { } kind)
      return this.BadRequestProblem ("This version of h2s doesn't know the panel's kind.");

    if (await this.CheckSingleInstanceAsync (kind) is { } taken)
      return this.BadRequestProblem (taken);

    List<Panel> zone = await this.context.Panels
                                 .Where (p => p.Zone == restore.Zone)
                                 .OrderBy (p => p.SortOrder)
                                 .ThenBy (p => p.Id)
                                 .ToListAsync ();

    var panel = new Panel
                {
                  Kind = kind.Key,
                  Zone = restore.Zone,

                  // Kept as it was, however it reads: the snapshot came from this panel.
                  OptionsJson = string.IsNullOrWhiteSpace (restore.OptionsJson) ? "{}" : restore.OptionsJson,
                };

    zone.Insert (index: Math.Clamp (value: restore.SortOrder, min: 0, max: zone.Count), item: panel);

    for (var i = 0; i < zone.Count; i++)
      zone[i].SortOrder = i;

    this.context.Panels.Add (panel);
    await this.context.SaveChangesAsync ();

    return EditorModel.PanelResult (panel: panel, kind: kind);
  }

  /// <summary>
  ///   Reorders the panels of one zone, moving any listed panels that sit in the other zone into it.
  /// </summary>
  /// <param name="zone">The zone whose panels are ordered: <c>Top</c> or <c>Bottom</c>.</param>
  /// <param name="orderedIds">
  ///   The IDs of every panel in the zone, plus any panels being moved into it, in the desired order.
  /// </param>
  /// <returns>A JSON payload confirming success, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostReorderPanelsAsync (string? zone, string[]? orderedIds)
  {
    if (!EditorModel.TryParseZone (value: zone, zone: out PanelZone target))
      return this.BadRequestProblem ("The zone must be Top or Bottom.");

    if (!OrderedIdParser.TryParse (values: orderedIds, ids: out List<int>? ids, error: out string? error))
      return this.BadRequestProblem (error);

    List<Panel>            panels     = await this.context.Panels.ToListAsync ();
    Dictionary<int, Panel> panelsById = panels.ToDictionary (p => p.Id);

    if (ids.Any (id => !panelsById.ContainsKey (id)))
      return this.BadRequestProblem ("One or more panels do not exist.");

    if (panels.Any (p => (p.Zone == target) && !ids.Contains (p.Id)))
      return this.BadRequestProblem ("The order must include every panel in the zone.");

    for (var i = 0; i < ids.Count; i++)
    {
      panelsById[ids[i]].Zone      = target;
      panelsById[ids[i]].SortOrder = i;
    }

    // Close the gap a panel moved in from the other zone leaves behind.
    var position = 0;

    foreach (Panel panel in panels.Where (p => p.Zone != target).OrderBy (p => p.SortOrder).ThenBy (p => p.Id))
      panel.SortOrder = position++;

    await this.context.SaveChangesAsync ();

    return new JsonResult (new { Success = true });
  }

  /// <summary>
  ///   Checks that a panel of a kind can be added: a single-instance kind can't when one is already on the dashboard.
  /// </summary>
  /// <param name="kind">The kind.</param>
  /// <returns>Why the panel can't be added, or <c>null</c> when it can.</returns>
  private async Task<string?> CheckSingleInstanceAsync (IPanelKind kind)
    => kind.IsSingleInstance && await this.context.Panels.AnyAsync (p => p.Kind == kind.Key)
         ? $"The dashboard already has a {kind.DisplayName.ToLowerInvariant ()} panel, and can only have one."
         : null;

  /// <summary>
  ///   Describes a kind's options for the panel dialog, which builds its form from them.
  /// </summary>
  /// <param name="options">The options.</param>
  /// <returns>One object per option, its type as <c>text</c>, <c>integer</c> or <c>boolean</c>.</returns>
  private static object[] DescribeOptions (IReadOnlyList<PanelOption> options)
    =>
    [
      .. options.Select (o => new
                              {
                                o.Key,
                                o.Label,
                                Type = o.Type.ToString ().ToLowerInvariant (),
                                o.HelpText,
                                o.IsRequired,
                                o.DefaultValue,
                                o.Minimum,
                                o.Maximum,
                                MaxLength = o.Type == PanelOptionType.Text ? PanelOptionForm.MaxTextLength : (int?)null,
                              }),
    ];

  /// <summary>
  ///   Describes a saved panel for the dialog's callers.
  /// </summary>
  /// <param name="panel">The panel.</param>
  /// <param name="kind">The panel's kind.</param>
  /// <returns>A JSON payload with the panel's ID, kind, name, zone (by name) and position.</returns>
  private static JsonResult PanelResult (Panel panel, IPanelKind kind)
    => new (new
            {
              panel.Id,
              panel.Kind,
              kind.DisplayName,
              Zone = panel.Zone.ToString (),
              panel.SortOrder,
            });

  /// <summary>
  ///   Parses a posted zone, by name only: <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)" /> would also take
  ///   numbers and comma-separated lists.
  /// </summary>
  /// <param name="value">The posted zone.</param>
  /// <param name="zone">The zone, when the value names one.</param>
  /// <returns><c>true</c> when it does.</returns>
  private static bool TryParseZone (string? value, out PanelZone zone)
  {
    string? name = Enum.GetNames<PanelZone> ()
                       .FirstOrDefault (n => string.Equals (a: n,
                                                            b: value?.Trim (),
                                                            comparisonType: StringComparison.OrdinalIgnoreCase));

    zone = name == null ? default (PanelZone) : Enum.Parse<PanelZone> (name);

    return name != null;
  }
}

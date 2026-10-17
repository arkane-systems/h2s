using h2s.Models;
using h2s.Options;
using h2s.Widgets;
using h2s.Widgets.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace h2s.Pages.Admin;

/// <summary>
///   The link dialog's widget handlers (<c>wwwroot/js/modules/widget-editor.js</c>). A link's widgets are posted with the
///   link, as a JSON <c>widgets</c> field <see cref="WidgetForm" /> reads, so they save, cancel and undo with it.
/// </summary>
public partial class EditorModel
{
  /// <summary>
  ///   Lists the widget kinds a link can have: those whose integration has a connection.
  /// </summary>
  /// <returns>
  ///   A JSON payload with each kind's reference (<c>integration/kind</c>), name, role (<c>app</c> or <c>status</c>),
  ///   option schema and default refresh interval in seconds.
  /// </returns>
  public async Task<IActionResult> OnGetWidgetKindsAsync ()
  {
    HashSet<string> connected =
    [
      .. (await this.connections.GetConnectionsAsync (this.HttpContext.RequestAborted)).Select (c => c.IntegrationKey),
    ];

    return new JsonResult (this.widgetCatalog
                               .Kinds
                               .Where (e => connected.Contains (e.Integration.Key))
                               .Select (e => new
                                             {
                                               e.Reference,
                                               e.DisplayName,
                                               Role                  = EditorModel.RoleName (e.Kind.Role),
                                               Options               = OptionDescriptions.Describe (e.Kind.Options),
                                               DefaultRefreshSeconds = (int)e.Kind.DefaultRefresh.TotalSeconds,
                                             }));
  }

  /// <summary>
  ///   Describes a link's widgets for the link dialog.
  /// </summary>
  /// <param name="widgets">The widgets, in order.</param>
  /// <param name="connections">The connections, by ID.</param>
  /// <returns>
  ///   One object per widget: its ID, kind reference, name, role and option values, and its refresh interval when it has
  ///   one of its own. A widget of a kind this version of h2s doesn't know says so, with no values.
  /// </returns>
  private object[] DescribeWidgets (IEnumerable<Widget> widgets, IReadOnlyDictionary<int, Connection> connections)
    =>
    [
      .. widgets.Select (w =>
                         {
                           Connection? connection = connections.GetValueOrDefault (w.ConnectionId);
                           WidgetKindEntry? entry = connection == null
                                                      ? null
                                                      : this.widgetCatalog.Find (integrationKey: connection.IntegrationKey,
                                                                                 kindKey: w.Kind);

                           return new
                                  {
                                    w.Id,
                                    Kind        = entry?.Reference   ?? w.Kind,
                                    DisplayName = entry?.DisplayName ?? $"Unknown widget ({w.Kind})",
                                    IsKnown     = entry != null,
                                    Role        = EditorModel.RoleName (entry?.Kind.Role ?? WidgetRole.Status),
                                    Values = entry == null
                                               ? new Dictionary<string, object?> ()
                                               : new OptionValues (schema: entry.Kind.Options, json: w.OptionsJson).Values,
                                    w.RefreshSeconds,
                                  };
                         }),
    ];

  /// <summary>
  ///   Applies the widgets posted with a link, if the form posted any: an older form without the field leaves the link's
  ///   widgets alone.
  /// </summary>
  /// <param name="link">The link, with its widgets loaded.</param>
  /// <returns>What's wrong with the posted widgets, or <c>null</c> when they were applied (or not posted).</returns>
  private async Task<string?> ApplyPostedWidgetsAsync (Link link)
  {
    if (!this.Request.HasFormContentType || !this.Request.Form.TryGetValue (key: "widgets", value: out StringValues posted))
    {
      return null;
    }

    if (!WidgetForm.TryRead (json: posted.ToString (),
                             catalog: this.widgetCatalog,
                             connections: await this.connections.GetConnectionsAsync (this.HttpContext.RequestAborted),
                             existing: link.Widgets,
                             plan: out List<WidgetPlan>? plan,
                             error: out string? error))
    {
      return error;
    }

    WidgetForm.Apply (link: link, plan: plan);

    return null;
  }

  /// <summary>
  ///   Recreates a snapshot's widgets on a restored link, leaving out any whose connection no longer exists.
  /// </summary>
  /// <param name="link">The restored link.</param>
  /// <param name="snapshot">The link's snapshot.</param>
  /// <param name="connectionIds">The connections that exist.</param>
  private static void RestoreWidgets (Link link, LinkSnapshot snapshot, IReadOnlySet<int> connectionIds)
    => link.Widgets.AddRange ((snapshot.Widgets ?? [])
                             .Where (w => connectionIds.Contains (w.ConnectionId) && !string.IsNullOrWhiteSpace (w.Kind))
                             .Select ((w, i) => new Widget
                                                {
                                                  ConnectionId   = w.ConnectionId,
                                                  Kind           = w.Kind!,
                                                  OptionsJson    = string.IsNullOrWhiteSpace (w.OptionsJson) ? "{}" : w.OptionsJson,
                                                  RefreshSeconds = w.RefreshSeconds,
                                                  SortOrder      = i,
                                                }));

  /// <summary>
  ///   Names a widget role as the browser does.
  /// </summary>
  /// <param name="role">The role.</param>
  /// <returns><c>app</c> or <c>status</c>.</returns>
  private static string RoleName (WidgetRole role) => role == WidgetRole.App ? "app" : "status";
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using h2s.Models;
using h2s.Options;
using h2s.Widgets.Abstractions;

namespace h2s.Widgets;

/// <summary>
///   Reads the widgets the link dialog posts with a link, as a JSON array in the order they show:
///   <c>[{ "id": 5, "kind": "uptime-kuma/monitor", "options": { "monitorId": "12" }, "refreshSeconds": "" }, …]</c>. An entry
///   with an <c>id</c> updates that widget of the link; one with <c>"keep": true</c> keeps it as it is (a widget of a kind
///   this version of h2s doesn't know); the link's other widgets are deleted.
/// </summary>
public static class WidgetForm
{
  /// <summary>
  ///   The most widgets a link may have.
  /// </summary>
  public const int MaxWidgetsPerLink = 10;

  /// <summary>
  ///   Checks posted widgets.
  /// </summary>
  /// <param name="json">The posted JSON array.</param>
  /// <param name="catalog">The known widget kinds.</param>
  /// <param name="connections">The connections; a widget uses its integration's first one.</param>
  /// <param name="existing">The link's widgets now; empty for a new link.</param>
  /// <param name="plan">The widgets to store, in order, when they're valid.</param>
  /// <param name="error">What's wrong with the first widget that isn't, when one isn't.</param>
  /// <returns><c>true</c> when every widget is valid.</returns>
  public static bool TryRead (string                                      json,
                              WidgetCatalog                               catalog,
                              IReadOnlyCollection<Connection>             connections,
                              IReadOnlyCollection<Widget>                 existing,
                              [NotNullWhen (true)] out  List<WidgetPlan>? plan,
                              [NotNullWhen (false)] out string?           error)
  {
    plan  = null;
    error = null;

    List<PostedWidget>? posted;

    try
    {
      posted = JsonSerializer.Deserialize<List<PostedWidget>> (json: json, options: JsonSerializerOptions.Web);
    }
    catch (JsonException)
    {
      posted = null;
    }

    if (posted == null)
    {
      error = "The link's widgets are malformed.";

      return false;
    }

    if (posted.Count > WidgetForm.MaxWidgetsPerLink)
    {
      error = $"A link can have at most {WidgetForm.MaxWidgetsPerLink} widgets.";

      return false;
    }

    Dictionary<int, Widget> existingById = existing.ToDictionary (w => w.Id);
    var                     result       = new List<WidgetPlan> (posted.Count);
    var                     appWidgets   = 0;

    for (var i = 0; i < posted.Count; i++)
    {
      PostedWidget item  = posted[i];
      Widget?      saved = item.Id is { } id ? existingById.GetValueOrDefault (id) : null;

      if (item.Keep)
      {
        if (saved != null)
        {
          result.Add (new WidgetPlan (ExistingId: saved.Id,
                                      ConnectionId: saved.ConnectionId,
                                      Kind: saved.Kind,
                                      OptionsJson: saved.OptionsJson,
                                      RefreshSeconds: saved.RefreshSeconds));
        }

        continue;
      }

      var where = $"Widget {i + 1}";

      if (catalog.Find (item.Kind ?? "") is not { } entry)
      {
        error = $"{where}: h2s has no such kind of widget.";

        return false;
      }

      where += $" ({entry.Kind.DisplayName})";

      if (connections.Where (c => c.IntegrationKey == entry.Integration.Key).MinBy (c => c.Id) is not { } connection)
      {
        error = $"{where}: {entry.Integration.DisplayName} isn't set up; add it in Settings → Integrations.";

        return false;
      }

      if ((entry.Kind.Role == WidgetRole.App) && (++appWidgets > 1))
      {
        error = $"{where}: a link can have only one app widget.";

        return false;
      }

      if (!OptionForm.TryParse (schema: entry.Kind.Options,
                                posted: item.Options,
                                json: out string? optionsJson,
                                error: out string? optionError))
      {
        error = $"{where}: {optionError}";

        return false;
      }

      if (!WidgetForm.TryParseRefresh (value: item.RefreshSeconds, seconds: out int? refresh))
      {
        error = $"{where}: the refresh interval must be a whole number of seconds, from {Widget.MinRefreshSeconds} to " +
                $"{Widget.MaxRefreshSeconds}.";

        return false;
      }

      result.Add (new WidgetPlan (ExistingId: saved?.Id,
                                  ConnectionId: connection.Id,
                                  Kind: entry.Kind.Key,
                                  OptionsJson: optionsJson,
                                  RefreshSeconds: refresh));
    }

    plan = result;

    return true;
  }

  /// <summary>
  ///   Brings a link's widgets in line with a checked plan: updating those it names, adding new ones, deleting the rest,
  ///   and numbering them in order. The link's widgets must be loaded.
  /// </summary>
  /// <param name="link">The link.</param>
  /// <param name="plan">The widgets to store, in order.</param>
  public static void Apply (Link link, IReadOnlyList<WidgetPlan> plan)
  {
    Dictionary<int, Widget> existing = link.Widgets.Where (w => w.Id != 0).ToDictionary (w => w.Id);
    var                     kept     = new HashSet<int> ();

    for (var i = 0; i < plan.Count; i++)
    {
      WidgetPlan item = plan[i];

      if (item.ExistingId is { } id && existing.TryGetValue (key: id, value: out Widget? widget))
      {
        _ = kept.Add (id);
      }
      else
      {
        widget = new Widget ();
        link.Widgets.Add (widget);
      }

      widget.ConnectionId   = item.ConnectionId;
      widget.Kind           = item.Kind;
      widget.OptionsJson    = item.OptionsJson;
      widget.RefreshSeconds = item.RefreshSeconds;
      widget.SortOrder      = i;
    }

    _ = link.Widgets.RemoveAll (w => (w.Id != 0) && !kept.Contains (w.Id));
  }

  /// <summary>
  ///   Reads a posted refresh interval.
  /// </summary>
  /// <param name="value">The posted value; blank for the kind's default.</param>
  /// <param name="seconds">The interval, or <c>null</c> for the default.</param>
  /// <returns><c>true</c> when it's blank or a valid interval.</returns>
  private static bool TryParseRefresh (string? value, out int? seconds)
  {
    seconds = null;

    if (string.IsNullOrWhiteSpace (value))
    {
      return true;
    }

    if (!int.TryParse (s: value.Trim (),
                       style: NumberStyles.None,
                       provider: CultureInfo.InvariantCulture,
                       result: out int parsed) ||
        (parsed < Widget.MinRefreshSeconds)    ||
        (parsed > Widget.MaxRefreshSeconds))
    {
      return false;
    }

    seconds = parsed;

    return true;
  }

  /// <summary>
  ///   A widget as the link dialog posts it.
  /// </summary>
  /// <param name="Id">The widget's ID, when it's already saved.</param>
  /// <param name="Kind">The kind's reference, <c>integration/kind</c>.</param>
  /// <param name="Options">The option values, as the form sends them.</param>
  /// <param name="RefreshSeconds">The refresh interval in seconds; blank for the kind's default.</param>
  /// <param name="Keep">Whether to keep the saved widget as it is.</param>
  private sealed record PostedWidget (
    int?                         Id,
    string?                      Kind,
    Dictionary<string, string?>? Options,
    string?                      RefreshSeconds,
    bool                         Keep);
}

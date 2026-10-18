using System.Globalization;
using System.Text.Json;
using h2s.Services;
using h2s.Widgets.Abstractions;

namespace h2s.Integrations.JsonApi;

/// <summary>
///   One value from a JSON API's answer, as a status pill: a request to a path under the connection's base URL, and a path
///   into the answer that picks the value. It shows the value with its unit, and can compare it with an expected one to
///   decide the pill's colour. <see cref="JsonApiClient" /> caches answers, so several widgets on one endpoint cost the
///   service one request per refresh.
/// </summary>
public sealed class JsonApiValueWidgetKind : IWidgetKind
{
  /// <summary>
  ///   The kind's key.
  /// </summary>
  public const string KindKey = "value";

  /// <summary>
  ///   The key of the option holding the path to request, relative to the connection's base URL.
  /// </summary>
  public const string PathOption = "path";

  /// <summary>
  ///   The key of the option holding the path to the value in the answer (<see cref="JsonValuePath" />).
  /// </summary>
  public const string ValuePathOption = "valuePath";

  /// <summary>
  ///   The key of the option holding what the value is called.
  /// </summary>
  public const string NameOption = "name";

  /// <summary>
  ///   The key of the option holding the value's unit.
  /// </summary>
  public const string UnitOption = "unit";

  /// <summary>
  ///   The key of the option holding the value the widget expects.
  /// </summary>
  public const string ExpectedOption = "expected";

  /// <summary>
  ///   The longest text the pill shows, in characters; a longer string is cut and ends with an ellipsis.
  /// </summary>
  public const int MaxTextLength = 40;

  /// <summary>
  ///   Cuts a string to <see cref="MaxTextLength" /> characters, adding an ellipsis when it was longer.
  /// </summary>
  /// <param name="text">The string.</param>
  /// <returns>The string, or its start and an ellipsis.</returns>
  public static string Shorten (string text)
  {
    if (text.Length <= JsonApiValueWidgetKind.MaxTextLength)
    {
      return text;
    }

    // Never cut between the halves of a surrogate pair.
    int length = char.IsHighSurrogate (text[JsonApiValueWidgetKind.MaxTextLength - 1])
                   ? JsonApiValueWidgetKind.MaxTextLength - 1
                   : JsonApiValueWidgetKind.MaxTextLength;

    return $"{text[..length].TrimEnd ()}…";
  }

  /// <inheritdoc />
  public string Key => JsonApiValueWidgetKind.KindKey;

  /// <inheritdoc />
  public string DisplayName => "Value";

  /// <inheritdoc />
  public WidgetRole Role => WidgetRole.Status;

  /// <inheritdoc />
  public TimeSpan DefaultRefresh => TimeSpan.FromSeconds (60);

  /// <inheritdoc />
  public IReadOnlyList<SchemaOption> Options { get; } =
    [
      new (Key: JsonApiValueWidgetKind.PathOption, Label: "Path", Type: OptionType.Text)
      {
        IsRequired  = true,
        Placeholder = "/status",
        HelpText    = "What to request, relative to the connection's base URL, with a query if it needs one: /json or status?full=1.",

        // Not a scheme and not //, so a path can't name another host; the fetch checks it again.
        Pattern            = @"(?!//)(?![A-Za-z][A-Za-z0-9+.\-]*:).*",
        PatternDescription = "must be a path under the base URL, not a full address",
      },
      new (Key: JsonApiValueWidgetKind.ValuePathOption, Label: "Value", Type: OptionType.Text)
      {
        IsRequired  = true,
        Placeholder = "slideshow.slides[1].title",
        HelpText = "Where the value is in the answer: keys joined by dots, [n] for the nth item of a list (from 0), and " +
                   "[\"odd key\"] for a key with dots, spaces or brackets. $ is the whole answer. A list shows its length.",
      },
      new (Key: JsonApiValueWidgetKind.NameOption, Label: "Name", Type: OptionType.Text)
      {
        Placeholder = "Slides", HelpText = "What the value is called in the tooltip; the value path if blank.",
      },
      new (Key: JsonApiValueWidgetKind.UnitOption, Label: "Unit", Type: OptionType.Text)
      {
        Placeholder = "ms", HelpText = "Shown after the value: ms, slides, %…",
      },
      new (Key: JsonApiValueWidgetKind.ExpectedOption, Label: "Expected value", Type: OptionType.Text)
      {
        Placeholder = "healthy",
        HelpText    = "When set, the pill is green if the value is exactly this and red if it isn't. Blank leaves it green.",
      },
    ];

  /// <inheritdoc />
  public async Task<WidgetResult> FetchAsync (WidgetContext context)
  {
    if (JsonApiSettings.From (context.Connection) is not { } settings)
    {
      return WidgetResult.Unknown ("The JSON API isn't set up.");
    }

    if (settings.AuthProblem is { } authProblem)
    {
      return WidgetResult.Unknown (authProblem);
    }

    string path          = context.Options.GetText (JsonApiValueWidgetKind.PathOption).Trim ();
    string valuePathText = context.Options.GetText (JsonApiValueWidgetKind.ValuePathOption).Trim ();

    if (path.Length == 0)
    {
      return WidgetResult.Unknown ("The widget has no path to request.");
    }

    if (!JsonValuePath.TryParse (text: valuePathText, path: out JsonValuePath? valuePath, error: out string? valuePathError))
    {
      return WidgetResult.Unknown ($"The value path isn't valid: {valuePathError}");
    }

    // Before any request, so a path can never send the connection's credentials anywhere else.
    if (!JsonApiUrls.TryResolve (baseUrl: settings.BaseUrl, path: path, url: out Uri? url, error: out string? pathError))
    {
      return WidgetResult.Unknown (pathError);
    }

    JsonApiResponse response = await context.Services.GetRequiredService<JsonApiClient> ()
                                            .GetAsync (settings: settings, url: url, cancellationToken: context.CancellationToken);

    if (response.IsUnreachable)
    {
      return WidgetResult.Unknown ($"Couldn't reach the JSON API at {url.Host}.");
    }

    if (response.IsTooLarge)
    {
      return JsonApiValueWidgetKind.Failed ($"The answer for {path} is larger than {JsonApiClient.MaxBodyBytes / 1024} KB.");
    }

    if (!response.IsSuccess)
    {
      return JsonApiValueWidgetKind.Failed ($"The API answered {response.Status} for {path}.");
    }

    JsonDocument document;

    try
    {
      document = JsonDocument.Parse (response.Body!);
    }
    catch (JsonException)
    {
      return JsonApiValueWidgetKind.Failed ($"The answer for {path} isn't JSON.");
    }

    using (document)
    {
      string name  = context.Options.GetText (JsonApiValueWidgetKind.NameOption).Trim ();
      string label = name.Length > 0 ? name : valuePathText.Length > 0 ? valuePathText : "$";

      return valuePath.TryEvaluate (root: document.RootElement, value: out JsonElement value)
               ? JsonApiValueWidgetKind.Describe (label: label, value: value, options: context.Options)
               : new WidgetResult (Status: WidgetStatus.Warning, Stats: [], Description: $"{label} not found in the response.");
    }
  }

  /// <summary>
  ///   Creates the result of a service that answered, but not with a usable document.
  /// </summary>
  /// <param name="description">What went wrong.</param>
  /// <returns>An error result.</returns>
  private static WidgetResult Failed (string description) => new (Status: WidgetStatus.Error, Stats: [], Description: description);

  /// <summary>
  ///   Describes a value: its figure, and its status, which an expected value decides.
  /// </summary>
  /// <param name="label">What the value is called.</param>
  /// <param name="value">The value.</param>
  /// <param name="options">The widget's options.</param>
  /// <returns>The result.</returns>
  private static WidgetResult Describe (string label, JsonElement value, OptionValues options)
  {
    string  unit = options.GetText (JsonApiValueWidgetKind.UnitOption).Trim ();
    string? text;

    switch (value.ValueKind)
    {
      case JsonValueKind.String:
        text = value.GetString ();

        break;

      case JsonValueKind.Number:
        text = value.GetRawText ();

        break;

      case JsonValueKind.True:
        text = "true";

        break;

      case JsonValueKind.False:
        text = "false";

        break;

      case JsonValueKind.Array:
        int count = value.GetArrayLength ();
        text = count.ToString (CultureInfo.InvariantCulture);

        if (unit.Length == 0)
        {
          unit = count == 1 ? "item" : "items";
        }

        break;

      case JsonValueKind.Object:
        return new WidgetResult (Status: WidgetStatus.Warning,
                                 Stats: [],
                                 Description: $"{label} is an object, not a single value; point the value path at one of its keys.");

      default:
        return new WidgetResult (Status: WidgetStatus.Warning, Stats: [], Description: $"{label} is null.");
    }

    var stat = new WidgetStat (Label: label, Value: JsonApiValueWidgetKind.Shorten (text ?? ""), Unit: unit.Length == 0 ? null : unit);
    string expected = options.GetText (JsonApiValueWidgetKind.ExpectedOption).Trim ();

    if (expected.Length == 0)
    {
      return new WidgetResult (Status: WidgetStatus.Ok, Stats: [stat], Description: $"{label} is {stat.Text}.");
    }

    return string.Equals (a: text?.Trim (), b: expected, comparisonType: StringComparison.Ordinal)
             ? new WidgetResult (Status: WidgetStatus.Ok, Stats: [stat], Description: $"{label} is {stat.Text}, as expected.")
             : new WidgetResult (Status: WidgetStatus.Error, Stats: [stat], Description: $"{label} is {stat.Text}, not {expected}.");
  }
}

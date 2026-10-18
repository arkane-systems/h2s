using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace h2s.Integrations.JsonApi;

/// <summary>
///   A path to one value in a JSON document, in a small syntax of its own: dot keys (<c>slideshow.title</c>), array indexes
///   (<c>slideshow.slides[1].title</c>) and quoted keys for names with dots, spaces or brackets
///   (<c>servers["web.1"].load</c>). An empty path, or <c>$</c>, is the whole document; a leading <c>$</c> before a key or
///   an index (<c>$.title</c>, <c>$[0]</c>) is allowed and means the same as leaving it out.
/// </summary>
public sealed class JsonValuePath
{
  /// <summary>
  ///   The path to the whole document.
  /// </summary>
  public static readonly JsonValuePath Whole = new ([]);

  private readonly IReadOnlyList<Step> steps;

  /// <summary>
  ///   Initializes a new instance of the <see cref="JsonValuePath" /> class.
  /// </summary>
  /// <param name="steps">The steps from the document's root to the value.</param>
  private JsonValuePath (IReadOnlyList<Step> steps) => this.steps = steps;

  /// <summary>
  ///   Reads a path.
  /// </summary>
  /// <param name="text">The path as written.</param>
  /// <param name="path">The path, when it's valid.</param>
  /// <param name="error">A sentence saying what's wrong, when it isn't.</param>
  /// <returns><c>true</c> when the path is valid.</returns>
  public static bool TryParse (string? text, [NotNullWhen (true)] out JsonValuePath? path, [NotNullWhen (false)] out string? error)
  {
    path = null;
    text = text?.Trim () ?? "";

    if ((text.Length == 0) || (text == "$"))
    {
      path  = JsonValuePath.Whole;
      error = null;

      return true;
    }

    var steps = new List<Step> ();
    var index = 0;

    // A leading $ is the root, but only when a dot or bracket follows; otherwise it's the start of a key.
    if ((text[0] == '$') && (text[1] is '.' or '['))
    {
      index = 1;
    }

    while (index < text.Length)
    {
      char c = text[index];

      if (c == '[')
      {
        if (!JsonValuePath.TryReadBracket (text: text, index: ref index, steps: steps, error: out error))
        {
          return false;
        }
      }
      else if ((c == '.') || (index == 0))
      {
        // A key follows a dot, or starts the path.
        if (c == '.')
        {
          index++;
        }

        int start = index;

        while ((index < text.Length) && (text[index] is not ('.' or '[' or ']')))
        {
          index++;
        }

        if (index == start)
        {
          error = index < text.Length && text[index] == ']'
                    ? $"Unexpected ']' at character {index + 1}."
                    : $"Expected a key at character {start + 1}.";

          return false;
        }

        steps.Add (new Step (Key: text[start..index], Index: 0));
      }
      else
      {
        error = $"Unexpected '{c}' at character {index + 1}; a key needs a '.' before it.";

        return false;
      }
    }

    path  = new JsonValuePath (steps);
    error = null;

    return true;
  }

  /// <summary>
  ///   Finds the value a path points to.
  /// </summary>
  /// <param name="root">The document's root.</param>
  /// <param name="value">The value, when the path leads to one.</param>
  /// <returns><c>true</c> when the document holds the value; <c>false</c> for a missing key or an index out of range.</returns>
  public bool TryEvaluate (JsonElement root, out JsonElement value)
  {
    value = root;

    foreach (Step step in this.steps)
    {
      if (step.Key != null)
      {
        if ((value.ValueKind != JsonValueKind.Object) || !value.TryGetProperty (propertyName: step.Key, value: out JsonElement next))
        {
          return false;
        }

        value = next;
      }
      else
      {
        if ((value.ValueKind != JsonValueKind.Array) || (step.Index >= value.GetArrayLength ()))
        {
          return false;
        }

        value = value[step.Index];
      }
    }

    return true;
  }

  /// <summary>
  ///   Reads a bracketed step, either an array index or a quoted key.
  /// </summary>
  /// <param name="text">The path.</param>
  /// <param name="index">Where the bracket opens; on success, just after the one that closes it.</param>
  /// <param name="steps">The steps so far, which this adds to.</param>
  /// <param name="error">What's wrong, when it isn't valid.</param>
  /// <returns><c>true</c> when the step is valid.</returns>
  private static bool TryReadBracket (string text, ref int index, List<Step> steps, [NotNullWhen (false)] out string? error)
  {
    int opened = index;
    index++;

    if ((index < text.Length) && (text[index] is '"' or '\''))
    {
      char quote = text[index];
      index++;

      var key = new StringBuilder ();

      while ((index < text.Length) && (text[index] != quote))
      {
        // A backslash takes the next character as it is, so a key can hold quotes and backslashes.
        if ((text[index] == '\\') && (index + 1 < text.Length))
        {
          index++;
        }

        _ = key.Append (text[index]);
        index++;
      }

      if (index >= text.Length)
      {
        error = $"The quoted key at character {opened + 1} never ends.";

        return false;
      }

      index++;
      steps.Add (new Step (Key: key.ToString (), Index: 0));
    }
    else
    {
      int start = index;

      while ((index < text.Length) && char.IsAsciiDigit (text[index]))
      {
        index++;
      }

      if (!int.TryParse (s: text[start..index], style: NumberStyles.None, provider: CultureInfo.InvariantCulture, result: out int number))
      {
        error = index == start
                  ? $"Expected an array index or a quoted key at character {start + 1}."
                  : $"The array index at character {start + 1} is too large.";

        return false;
      }

      steps.Add (new Step (Key: null, Index: number));
    }

    if ((index >= text.Length) || (text[index] != ']'))
    {
      error = $"The '[' at character {opened + 1} isn't closed with ']'.";

      return false;
    }

    index++;
    error = null;

    return true;
  }

  /// <summary>
  ///   One step down a path: into an object by key, or into an array by index.
  /// </summary>
  /// <param name="Key">The key, or <c>null</c> for an index.</param>
  /// <param name="Index">The index, when there's no key.</param>
  private sealed record Step (string? Key, int Index);
}

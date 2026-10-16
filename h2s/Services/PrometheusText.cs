using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace h2s.Services;

/// <summary>
///   Reads the Prometheus text exposition format, as served by <c>/metrics</c> endpoints: one sample per line, as
///   <c>name{label="value",…} value [timestamp]</c>, with <c>#</c> comments.
/// </summary>
public static class PrometheusText
{
  /// <summary>
  ///   Reads every sample in a document. Lines that aren't valid samples are skipped.
  /// </summary>
  /// <param name="text">The document.</param>
  /// <returns>The samples, in document order.</returns>
  public static List<PrometheusSample> Parse (string text)
  {
    var samples = new List<PrometheusSample> ();

    foreach (string rawLine in text.Split ('\n'))
    {
      string line = rawLine.Trim ();

      if ((line.Length > 0) && (line[0] != '#') && PrometheusText.TryParseLine (line: line, sample: out PrometheusSample? sample))
        samples.Add (sample);
    }

    return samples;
  }

  /// <summary>
  ///   Reads one sample line.
  /// </summary>
  /// <param name="line">The line, trimmed and not a comment.</param>
  /// <param name="sample">The sample, when the line is one.</param>
  /// <returns><c>true</c> when it is.</returns>
  private static bool TryParseLine (string line, [NotNullWhen (true)] out PrometheusSample? sample)
  {
    sample = null;

    var position = 0;

    while ((position < line.Length) && (char.IsAsciiLetterOrDigit (line[position]) || line[position] is '_' or ':'))
      position++;

    if (position == 0)
      return false;

    string name   = line[..position];
    var    labels = new Dictionary<string, string> (StringComparer.Ordinal);

    if ((position < line.Length) && (line[position] == '{'))
    {
      position++;

      if (!PrometheusText.TryParseLabels (line: line, position: ref position, labels: labels))
        return false;
    }

    // The value, then an optional timestamp, which is ignored.
    string[] rest = line[position..].Split (separator: ' ', options: StringSplitOptions.RemoveEmptyEntries);

    if ((rest.Length == 0) || !PrometheusText.TryParseValue (text: rest[0], value: out double value))
      return false;

    sample = new PrometheusSample (Name: name, Labels: labels, Value: value);

    return true;
  }

  /// <summary>
  ///   Reads a label set, from just after its <c>{</c> to just after its <c>}</c>.
  /// </summary>
  /// <param name="line">The line.</param>
  /// <param name="position">Where the labels start; moved past the closing brace.</param>
  /// <param name="labels">The dictionary the labels are added to.</param>
  /// <returns><c>true</c> when the label set is well formed.</returns>
  private static bool TryParseLabels (string line, ref int position, Dictionary<string, string> labels)
  {
    while (position < line.Length)
    {
      while ((position < line.Length) && line[position] is ' ' or ',')
        position++;

      if ((position < line.Length) && (line[position] == '}'))
      {
        position++;

        return true;
      }

      int start = position;

      while ((position < line.Length) && (char.IsAsciiLetterOrDigit (line[position]) || (line[position] == '_')))
        position++;

      string key = line[start..position];

      if ((key.Length == 0) || (position + 1 >= line.Length) || (line[position] != '=') || (line[position + 1] != '"'))
        return false;

      position += 2;

      var value = new StringBuilder ();

      while ((position < line.Length) && (line[position] != '"'))
      {
        if ((line[position] == '\\') && (position + 1 < line.Length))
        {
          position++;
          _ = value.Append (line[position] == 'n' ? '\n' : line[position]);
        }
        else
          _ = value.Append (line[position]);

        position++;
      }

      if (position >= line.Length)
        return false;

      position++;
      labels[key] = value.ToString ();
    }

    return false;
  }

  /// <summary>
  ///   Reads a sample value, including the special values <c>NaN</c>, <c>+Inf</c> and <c>-Inf</c>.
  /// </summary>
  /// <param name="text">The value as written.</param>
  /// <param name="value">The value.</param>
  /// <returns><c>true</c> when it's a number.</returns>
  private static bool TryParseValue (string text, out double value)
  {
    switch (text)
    {
      case "NaN":
        value = double.NaN;

        return true;

      case "+Inf":
        value = double.PositiveInfinity;

        return true;

      case "-Inf":
        value = double.NegativeInfinity;

        return true;

      default:
        return double.TryParse (s: text, style: NumberStyles.Float, provider: CultureInfo.InvariantCulture, result: out value);
    }
  }
}

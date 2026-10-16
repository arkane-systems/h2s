using h2s.Widgets.Abstractions;

namespace h2s.Options;

/// <summary>
///   Describes option schemas for the browser, where <c>wwwroot/js/modules/option-fields.js</c> builds the edit form's
///   fields from them.
/// </summary>
public static class OptionDescriptions
{
  /// <summary>
  ///   Describes a schema.
  /// </summary>
  /// <param name="options">The options.</param>
  /// <returns>
  ///   One object per option, its type as <c>text</c>, <c>url</c>, <c>integer</c>, <c>boolean</c>, <c>secret</c> or
  ///   <c>select</c> (with its choices). A secret's description carries no value.
  /// </returns>
  public static object[] Describe (IReadOnlyList<SchemaOption> options)
    =>
    [
      .. options.Select (o => new
                              {
                                o.Key,
                                o.Label,
                                Type = o.Type.ToString ().ToLowerInvariant (),
                                o.HelpText,
                                o.IsRequired,
                                DefaultValue = o.Type == OptionType.Secret ? null : o.DefaultValue,
                                o.Minimum,
                                o.Maximum,
                                MaxLength = o.Type switch
                                            {
                                              OptionType.Text   => OptionForm.MaxTextLength,
                                              OptionType.Url    => OptionForm.MaxUrlLength,
                                              OptionType.Secret => OptionForm.MaxSecretLength,
                                              _                 => (int?)null,
                                            },
                                o.Pattern,
                                o.PatternDescription,
                                o.Placeholder,
                                Choices = o.Choices.Select (c => new { c.Value, c.Label }),
                              }),
    ];
}

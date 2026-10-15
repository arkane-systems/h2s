using System.Reflection;

namespace h2s.Services;

/// <summary>
///   The application's version, as shown in the footer. It comes from <c>&lt;Version&gt;</c> in <c>h2s.csproj</c>, which the
///   publish workflow requires to match the release tag.
/// </summary>
public static class AppVersion
{
  /// <summary>
  ///   The version of the running application, such as <c>0.4</c>.
  /// </summary>
  public static string Current { get; } =
    AppVersion.FromInformationalVersion (typeof (AppVersion).Assembly
                                                            .GetCustomAttribute<AssemblyInformationalVersionAttribute> ()
                                                           ?.InformationalVersion);

  /// <summary>
  ///   Reduces an informational version to the plain version, dropping the source revision the SDK appends (<c>+sha</c>).
  /// </summary>
  /// <param name="informationalVersion">The assembly's informational version, if any.</param>
  /// <returns>The version, or an empty string when there is none.</returns>
  public static string FromInformationalVersion (string? informationalVersion)
    => informationalVersion?.Split ('+')[0].Trim () ?? string.Empty;
}

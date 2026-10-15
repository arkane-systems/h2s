using h2s.Models;

namespace h2s.Services;

/// <summary>
///   Preferences each browser keeps for itself, in cookies the server reads so it can render them without a flash. The
///   client sets them (<c>_Layout.cshtml</c>'s color mode toggle); nothing here is stored on the server.
/// </summary>
public static class ViewerPreferences
{
  /// <summary>
  ///   The cookie holding this browser's color mode: <c>auto</c>, <c>light</c> or <c>dark</c>. Without it, the browser
  ///   follows the site's color mode from Settings → Appearance.
  /// </summary>
  public const string ColorModeCookie = "h2s-color-mode";

  /// <summary>
  ///   Reads this browser's color mode.
  /// </summary>
  /// <param name="request">The request.</param>
  /// <returns>The browser's own color mode, or <c>null</c> when it follows the site's (or the cookie is unreadable).</returns>
  public static ColorMode? ReadColorMode (HttpRequest request)
    => request.Cookies[ViewerPreferences.ColorModeCookie]?.Trim ().ToLowerInvariant () switch
       {
         "auto"  => ColorMode.Auto,
         "light" => ColorMode.Light,
         "dark"  => ColorMode.Dark,
         _       => null,
       };

  /// <summary>
  ///   Describes the color mode toggle's state: its icon, and a title that says what a click does next. The toggle cycles
  ///   the site default, then light, dark and auto for this browser, then back to the site default.
  /// </summary>
  /// <param name="viewerMode">This browser's color mode, or <c>null</c> when it follows the site's.</param>
  /// <param name="siteMode">The site's color mode.</param>
  /// <returns>The icon and title.</returns>
  public static (string Icon, string Title) DescribeToggle (ColorMode? viewerMode, ColorMode siteMode)
    => viewerMode switch
       {
         null            => (ViewerPreferences.IconOf (siteMode), $"Site default ({siteMode}). Click for light in this browser."),
         ColorMode.Light => ("☀", "Light in this browser. Click for dark."),
         ColorMode.Dark  => ("☾", "Dark in this browser. Click for auto."),
         _               => ("◑", "Auto in this browser. Click to follow the site default."),
       };

  /// <summary>
  ///   Gets a color mode's icon.
  /// </summary>
  /// <param name="mode">The mode.</param>
  /// <returns>☀ for light, ☾ for dark, and ◑ for auto.</returns>
  private static string IconOf (ColorMode mode)
    => mode switch
       {
         ColorMode.Light => "☀",
         ColorMode.Dark  => "☾",
         _               => "◑",
       };
}

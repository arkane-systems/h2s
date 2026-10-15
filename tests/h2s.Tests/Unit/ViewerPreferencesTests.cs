using h2s.Models;
using h2s.Services;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for <see cref="ViewerPreferences" />.
/// </summary>
public class ViewerPreferencesTests
{
  [Theory]
  [InlineData (null,            ColorMode.Auto,  "◑", "Site default (Auto). Click for light in this browser.")]
  [InlineData (null,            ColorMode.Dark,  "☾", "Site default (Dark). Click for light in this browser.")]
  [InlineData (ColorMode.Light, ColorMode.Dark,  "☀", "Light in this browser. Click for dark.")]
  [InlineData (ColorMode.Dark,  ColorMode.Light, "☾", "Dark in this browser. Click for auto.")]
  [InlineData (ColorMode.Auto,  ColorMode.Light, "◑", "Auto in this browser. Click to follow the site default.")]
  public void DescribeToggle_SaysWhatAClickDoesNext (ColorMode? viewerMode, ColorMode siteMode, string icon, string title)
    => Assert.Equal (expected: (icon, title),
                     actual: ViewerPreferences.DescribeToggle (viewerMode: viewerMode, siteMode: siteMode));
}

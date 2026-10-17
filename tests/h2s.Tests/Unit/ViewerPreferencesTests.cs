using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Http;

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

  [Theory]
  [InlineData (null,             new int[0])]
  [InlineData ("",               new int[0])]
  [InlineData ("3",              new[] { 3 })]
  [InlineData ("3.12.3",         new[] { 3, 12 })]
  [InlineData ("0.-1.x.007.2x5", new[] { 7 })]
  [InlineData ("..4..",          new[] { 4 })]
  public void ReadCollapsedCategories_KeepsPositiveIds (string? cookie, int[] expected)
  {
    var context = new DefaultHttpContext ();

    if (cookie != null)
    {
      context.Request.Headers.Cookie = $"{ViewerPreferences.CollapsedCategoriesCookie}={cookie}";
    }

    Assert.Equal (expected: expected.Order (), actual: ViewerPreferences.ReadCollapsedCategories (context.Request).Order ());
  }
}

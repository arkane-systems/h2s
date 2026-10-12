using h2s.Models;

namespace h2s.Tests.Unit;

/// <summary>
/// Tests for the icon helpers on <see cref="Link"/>.
/// </summary>
public class LinkTests
{
  private const string IconsBaseUrl = "https://cdn.jsdelivr.net/gh/selfhst/icons";

  [Theory]
  [InlineData ("png", "png")]
  [InlineData ("SVG", "svg")]
  [InlineData (" webp ", "webp")]
  [InlineData ("gif", "png")]
  public void BuildIconUrl_NormalizesFormat (string format, string expectedFormat)
    => Assert.Equal ($"{LinkTests.IconsBaseUrl}/{expectedFormat}/jellyfin.{expectedFormat}", Link.BuildIconUrl ("jellyfin", format));

  [Fact]
  public void BuildIconUrl_NormalizesIconName ()
    => Assert.Equal ($"{LinkTests.IconsBaseUrl}/png/home-assistant.png", Link.BuildIconUrl ("Home Assistant"));

  [Theory]
  [InlineData ("")]
  [InlineData ("   ")]
  [InlineData ("---")]
  public void BuildIconUrl_ReturnsEmptyForBlankName (string iconName) => Assert.Equal ("", Link.BuildIconUrl (iconName));

  [Fact]
  public void GetIconUrl_UsesIconName ()
  {
    var link = new Link { IconName = "pi-hole" };

    Assert.Equal ($"{LinkTests.IconsBaseUrl}/svg/pi-hole.svg", link.GetIconUrl ("svg"));
  }

  [Theory]
  [InlineData ("Home Assistant", "home-assistant")]
  [InlineData ("  Pi-hole  ", "pi-hole")]
  [InlineData ("TrueNAS_Scale!!", "truenas-scale")]
  [InlineData ("--Paperless   NGX--", "paperless-ngx")]
  [InlineData ("jellyfin", "jellyfin")]
  [InlineData ("", "")]
  [InlineData ("***", "")]
  public void NormalizeIconName_Slugifies (string input, string expected) => Assert.Equal (expected, Link.NormalizeIconName (input));
}

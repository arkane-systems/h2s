using h2s.Models;

namespace h2s.Tests.Unit;

/// <summary>
///   Tests for the icon helpers on <see cref="Link" />.
/// </summary>
public class LinkTests
{
  private const string IconsBaseUrl = "https://cdn.jsdelivr.net/gh/selfhst/icons";

  [Theory]
  [InlineData ("png",    "png")]
  [InlineData ("SVG",    "svg")]
  [InlineData (" webp ", "webp")]
  [InlineData ("gif",    "png")]
  public void BuildIconUrl_NormalizesFormat (string format, string expectedFormat)
    => Assert.Equal (expected: $"{LinkTests.IconsBaseUrl}/{expectedFormat}/jellyfin.{expectedFormat}",
                     actual: Link.BuildIconUrl (iconName: "jellyfin", format: format));

  [Fact]
  public void BuildIconUrl_NormalizesIconName ()
    => Assert.Equal (expected: $"{LinkTests.IconsBaseUrl}/png/home-assistant.png", actual: Link.BuildIconUrl ("Home Assistant"));

  [Theory]
  [InlineData ("")]
  [InlineData ("   ")]
  [InlineData ("---")]
  public void BuildIconUrl_ReturnsEmptyForBlankName (string iconName)
    => Assert.Equal (expected: "", actual: Link.BuildIconUrl (iconName));

  [Fact]
  public void GetIconUrl_UsesIconName ()
  {
    var link = new Link { IconName = "pi-hole" };

    Assert.Equal (expected: $"{LinkTests.IconsBaseUrl}/svg/pi-hole.svg", actual: link.GetIconUrl ("svg"));
  }

  [Fact]
  public void GetIconUrl_FallsBackToIconUrl ()
  {
    var link = new Link { IconUrl = "https://hub.docker.com/favicon.ico" };

    Assert.Equal (expected: "https://hub.docker.com/favicon.ico", actual: link.GetIconUrl ());
  }

  [Fact]
  public void GetIconUrl_PrefersIconNameToIconUrl ()
  {
    var link = new Link { IconName = "docker", IconUrl = "https://hub.docker.com/favicon.ico" };

    Assert.Equal (expected: $"{LinkTests.IconsBaseUrl}/png/docker.png", actual: link.GetIconUrl ());
  }

  [Theory]
  [InlineData ("Home Assistant",      "home-assistant")]
  [InlineData ("  Pi-hole  ",         "pi-hole")]
  [InlineData ("TrueNAS_Scale!!",     "truenas-scale")]
  [InlineData ("--Paperless   NGX--", "paperless-ngx")]
  [InlineData ("jellyfin",            "jellyfin")]
  [InlineData ("",                    "")]
  [InlineData ("***",                 "")]
  public void NormalizeIconName_Slugifies (string input, string expected)
    => Assert.Equal (expected: expected, actual: Link.NormalizeIconName (input));
}

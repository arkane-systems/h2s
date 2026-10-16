using System.Text.Json;
using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace h2s.Pages;

/// <summary>
///   The web app manifest (<c>/manifest.webmanifest</c>), which lets browsers install h2s as an app that opens in its own
///   window. It's built per request so its name follows Settings → General. There's no service worker: a dashboard of
///   homelab links is no use offline.
/// </summary>
public class ManifestModel : PageModel
{
  /// <summary>
  ///   The manifest's media type.
  /// </summary>
  public const string ContentType = "application/manifest+json";

  /// <summary>
  ///   The navbar's colour in the light theme (Bootstrap's tertiary background), for the title bar of an installed app.
  ///   The layout's <c>theme-color</c> meta tags give the dark theme's too.
  /// </summary>
  public const string LightThemeColor = "#f8f9fa";

  /// <summary>
  ///   The navbar's colour in the dark theme.
  /// </summary>
  public const string DarkThemeColor = "#2b3035";

  /// <summary>
  ///   The dashboard background's indigo, behind the icon on an installed app's splash screen (and the maskable icon).
  /// </summary>
  public const string SplashColor = "#1e1b4b";

  // Manifest members are snake_case.
  private static readonly JsonSerializerOptions JsonOptions = new () { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

  private readonly DashboardSettingsService settingsService;

  /// <summary>
  ///   Initializes a new instance of the <see cref="ManifestModel" /> class.
  /// </summary>
  /// <param name="settingsService">The service used to read the dashboard's title and motto.</param>
  public ManifestModel (DashboardSettingsService settingsService) => this.settingsService = settingsService;

  /// <summary>
  ///   Returns the manifest.
  /// </summary>
  /// <returns>The manifest, as JSON.</returns>
  public async Task<IActionResult> OnGetAsync ()
  {
    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();
    string            root     = this.Url.Content ("~/");
    string            icons    = this.Url.Content ("~/icons/");

    var manifest = new
                   {
                     Name            = $"h2s - {settings.Title}",
                     ShortName       = settings.Title,
                     Description     = string.IsNullOrWhiteSpace (settings.Motto) ? "Homelab link dashboard" : settings.Motto,
                     Id              = root,
                     StartUrl        = root,
                     Scope           = root,
                     Display         = "standalone",
                     BackgroundColor = ManifestModel.SplashColor,
                     ThemeColor      = ManifestModel.LightThemeColor,
                     Icons = new[]
                             {
                               new { Src = $"{icons}icon-192.png", Sizes = "192x192", Type = "image/png", Purpose = "any" },
                               new { Src = $"{icons}icon-512.png", Sizes = "512x512", Type = "image/png", Purpose = "any" },
                               new
                               {
                                 Src = $"{icons}icon-maskable-512.png", Sizes = "512x512", Type = "image/png", Purpose = "maskable",
                               },
                             },
                   };

    return this.Content (content: JsonSerializer.Serialize (value: manifest, options: ManifestModel.JsonOptions),
                         contentType: ManifestModel.ContentType);
  }
}

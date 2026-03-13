using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;

namespace h2s.Pages;

/// <summary>
/// Page model for the shared error page.
/// </summary>
[ResponseCache (Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
  /// <summary>
  /// Gets or sets the request identifier shown on the error page.
  /// </summary>
  public string? RequestId { get; set; }

  /// <summary>
  /// Gets a value indicating whether a request identifier is available to display.
  /// </summary>
  public bool ShowRequestId => !string.IsNullOrEmpty (RequestId);

  /// <summary>
  /// Captures the current request identifier for display on the error page.
  /// </summary>
  public void OnGet ()
  {
    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
  }
}


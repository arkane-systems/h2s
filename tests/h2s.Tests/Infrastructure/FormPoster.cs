using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace h2s.Tests.Infrastructure;

/// <summary>
/// Helpers for posting forms to Razor Pages handlers with a valid antiforgery token.
/// </summary>
public static partial class FormPoster
{
  /// <summary>
  /// Fetches a page and extracts the antiforgery request token from its hidden form field. The client must keep
  /// cookies (the <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> default) so that
  /// the matching antiforgery cookie is sent with later requests.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="pagePath">The path of a page that renders an antiforgery token.</param>
  /// <returns>The request token.</returns>
  public static async Task<string> GetAntiforgeryTokenAsync (HttpClient client, string pagePath)
  {
    var html  = await client.GetStringAsync (pagePath, TestContext.Current.CancellationToken);
    var match = FormPoster.AntiforgeryTokenPattern ().Match (html);

    Assert.True (match.Success, $"No antiforgery token found on {pagePath}.");

    return match.Groups["token"].Value;
  }

  /// <summary>
  /// Posts form fields to a page handler, including an antiforgery token obtained from the same page.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="pagePath">The page path, used both to obtain the token and as the post target.</param>
  /// <param name="handler">The named page handler, or <c>null</c> for the default POST handler.</param>
  /// <param name="fields">The form fields to post.</param>
  /// <returns>The response.</returns>
  public static async Task<HttpResponseMessage> PostAsync (HttpClient client,
                                                           string pagePath,
                                                           string? handler,
                                                           IEnumerable<KeyValuePair<string, string>> fields)
  {
    var token  = await FormPoster.GetAntiforgeryTokenAsync (client, pagePath);
    var target = handler is null ? pagePath : $"{pagePath}?handler={handler}";
    var form   = new FormUrlEncodedContent (fields.Append (new ("__RequestVerificationToken", token)));

    return await client.PostAsync (target, form, TestContext.Current.CancellationToken);
  }

  /// <summary>
  /// Reads a response body as a JSON document.
  /// </summary>
  /// <param name="response">The response.</param>
  /// <returns>The root JSON element.</returns>
  public static async Task<JsonElement> ReadJsonAsync (HttpResponseMessage response)
    => await response.Content.ReadFromJsonAsync<JsonElement> (TestContext.Current.CancellationToken);

  [GeneratedRegex ("""<input[^>]*name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""")]
  private static partial Regex AntiforgeryTokenPattern ();
}

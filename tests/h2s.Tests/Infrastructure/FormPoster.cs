using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace h2s.Tests.Infrastructure;

/// <summary>
///   Helpers for posting forms to Razor Pages handlers with a valid antiforgery token.
/// </summary>
public static partial class FormPoster
{
  /// <summary>
  ///   Fetches a page and extracts the antiforgery request token from its hidden form field. The client must keep
  ///   cookies (the <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}" /> default) so that
  ///   the matching antiforgery cookie is sent with later requests.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="pagePath">The path of a page that renders an antiforgery token.</param>
  /// <returns>The request token.</returns>
  public static async Task<string> GetAntiforgeryTokenAsync (HttpClient client, string pagePath)
  {
    string html  = await client.GetStringAsync (requestUri: pagePath, cancellationToken: TestContext.Current.CancellationToken);
    Match  match = FormPoster.AntiforgeryTokenPattern ().Match (html);

    Assert.True (condition: match.Success, userMessage: $"No antiforgery token found on {pagePath}.");

    return match.Groups["token"].Value;
  }

  /// <summary>
  ///   Posts form fields to a page handler, including an antiforgery token obtained from the same page.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="pagePath">The page path, used both to obtain the token and as the post target.</param>
  /// <param name="handler">The named page handler, or <c>null</c> for the default POST handler.</param>
  /// <param name="fields">The form fields to post.</param>
  /// <returns>The response.</returns>
  public static async Task<HttpResponseMessage> PostAsync (HttpClient                                client,
                                                           string                                    pagePath,
                                                           string?                                   handler,
                                                           IEnumerable<KeyValuePair<string, string>> fields)
  {
    string token  = await FormPoster.GetAntiforgeryTokenAsync (client: client, pagePath: pagePath);
    string target = handler is null ? pagePath : $"{pagePath}?handler={handler}";
    var form =
      new FormUrlEncodedContent (fields.Append (new KeyValuePair<string, string> (key: "__RequestVerificationToken",
                                                                                  value: token)));

    return await client.PostAsync (requestUri: target, content: form, cancellationToken: TestContext.Current.CancellationToken);
  }

  /// <summary>
  ///   Posts a file as multipart form data to a page handler, including an antiforgery token obtained from the same page.
  /// </summary>
  /// <param name="client">The test client.</param>
  /// <param name="pagePath">The page path, used both to obtain the token and as the post target.</param>
  /// <param name="handler">The named page handler.</param>
  /// <param name="fieldName">The form field the file is posted as.</param>
  /// <param name="fileName">The file's name.</param>
  /// <param name="contents">The file's contents.</param>
  /// <returns>The response.</returns>
  public static async Task<HttpResponseMessage> PostFileAsync (HttpClient client,
                                                               string     pagePath,
                                                               string     handler,
                                                               string     fieldName,
                                                               string     fileName,
                                                               byte[]     contents)
  {
    string token = await FormPoster.GetAntiforgeryTokenAsync (client: client, pagePath: pagePath);

    using var form = new MultipartFormDataContent ();
    form.Add (content: new StringContent (token),       name: "__RequestVerificationToken");
    form.Add (content: new ByteArrayContent (contents), name: fieldName, fileName: fileName);

    return await client.PostAsync (requestUri: $"{pagePath}?handler={handler}",
                                   content: form,
                                   cancellationToken: TestContext.Current.CancellationToken);
  }

  /// <summary>
  ///   Reads a response body as a JSON document.
  /// </summary>
  /// <param name="response">The response.</param>
  /// <returns>The root JSON element.</returns>
  public static async Task<JsonElement> ReadJsonAsync (HttpResponseMessage response)
    => await response.Content.ReadFromJsonAsync<JsonElement> (TestContext.Current.CancellationToken);

  /// <summary>
  ///   Asserts that a response is an RFC 9457 problem with the given status and detail, and returns its body.
  /// </summary>
  /// <param name="response">The response.</param>
  /// <param name="status">The expected HTTP status.</param>
  /// <param name="detail">The expected problem detail.</param>
  /// <returns>The problem document.</returns>
  public static async Task<JsonElement> AssertProblemAsync (HttpResponseMessage response, HttpStatusCode status, string detail)
  {
    Assert.Equal (expected: status,                     actual: response.StatusCode);
    Assert.Equal (expected: "application/problem+json", actual: response.Content.Headers.ContentType?.MediaType);

    JsonElement problem = await FormPoster.ReadJsonAsync (response);

    Assert.Equal (expected: (int)status, actual: problem.GetProperty ("status").GetInt32 ());
    Assert.Equal (expected: detail,      actual: problem.GetProperty ("detail").GetString ());

    return problem;
  }

  [GeneratedRegex ("""<input[^>]*name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""")]
  private static partial Regex AntiforgeryTokenPattern ();
}

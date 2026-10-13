using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace h2s.Pages;

/// <summary>
///   Builds RFC 9457 problem responses (<c>application/problem+json</c>) for the JSON page handlers, so the client
///   scripts get one error shape from every handler.
/// </summary>
public static class PageModelProblemExtensions
{
  private const string ProblemContentType = "application/problem+json";

  /// <summary>
  ///   Creates a 400 Bad Request problem response.
  /// </summary>
  /// <param name="page">The page model handling the request.</param>
  /// <param name="detail">A human-readable explanation of what was wrong with the request.</param>
  /// <returns>The problem response.</returns>
  public static ObjectResult BadRequestProblem (this PageModel page, string detail)
    => PageModelProblemExtensions.Problem (page: page, statusCode: StatusCodes.Status400BadRequest, detail: detail);

  /// <summary>
  ///   Creates a 404 Not Found problem response.
  /// </summary>
  /// <param name="page">The page model handling the request.</param>
  /// <param name="detail">A human-readable explanation of what could not be found.</param>
  /// <returns>The problem response.</returns>
  public static ObjectResult NotFoundProblem (this PageModel page, string detail)
    => PageModelProblemExtensions.Problem (page: page, statusCode: StatusCodes.Status404NotFound, detail: detail);

  /// <summary>
  ///   Creates a problem response with the given status code, using the application's <see cref="ProblemDetailsFactory" />
  ///   so that the title, type and trace identifier follow the framework defaults.
  /// </summary>
  /// <param name="page">The page model handling the request.</param>
  /// <param name="statusCode">The HTTP status code.</param>
  /// <param name="detail">A human-readable explanation of the problem.</param>
  /// <returns>The problem response.</returns>
  private static ObjectResult Problem (PageModel page, int statusCode, string detail)
  {
    ProblemDetails problem = page.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory> ()
                                 .CreateProblemDetails (httpContext: page.HttpContext, statusCode: statusCode, detail: detail);

    return new ObjectResult (problem) { StatusCode = statusCode, ContentTypes = { PageModelProblemExtensions.ProblemContentType } };
  }
}

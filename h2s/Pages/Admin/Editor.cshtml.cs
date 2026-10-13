using h2s.Data;
using h2s.Models;
using h2s.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages.Admin;

/// <summary>
///   Page model for the admin editor used to manage categories and links.
/// </summary>
public class EditorModel : PageModel
{
  private readonly DashboardContext         context;
  private readonly IconCatalog              iconCatalog;
  private readonly DashboardSettingsService settingsService;

  /// <summary>
  ///   Initializes a new instance of the <see cref="EditorModel" /> class.
  /// </summary>
  /// <param name="context">The database context used to manage categories and links.</param>
  /// <param name="iconCatalog">The catalog used to suggest icons for link labels.</param>
  /// <param name="settingsService">The service used to load dashboard settings.</param>
  public EditorModel (DashboardContext context, IconCatalog iconCatalog, DashboardSettingsService settingsService)
  {
    this.context         = context;
    this.iconCatalog     = iconCatalog;
    this.settingsService = settingsService;
  }

  /// <summary>
  ///   Gets the categories shown in the editor UI.
  /// </summary>
  public List<Category> Categories { get; private set; } = new ();

  /// <summary>
  ///   Gets the links shown in the editor UI.
  /// </summary>
  public List<Link> Links { get; private set; } = new ();

  /// <summary>
  ///   Gets a value indicating whether Uptime Kuma integration is configured.
  /// </summary>
  public bool IsUptimeKumaConfigured { get; private set; }

  /// <summary>
  ///   Loads the initial category and link data required by the editor page.
  /// </summary>
  public async Task OnGetAsync ()
  {
    this.Categories = await this.GetOrderedCategoriesQuery ()
                                .AsNoTracking ()
                                .ToListAsync ();

    this.Links = await this.GetOrderedLinksQuery ()
                           .AsNoTracking ()
                           .ToListAsync ();

    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();
    this.IsUptimeKumaConfigured = !string.IsNullOrWhiteSpace (settings.UptimeKumaServerUrl);
  }

  /// <summary>
  ///   Returns the ordered category list used by the editor client script.
  /// </summary>
  /// <returns>A JSON payload describing all categories.</returns>
  public async Task<IActionResult> OnGetCategoriesAsync ()
  {
    var categories = await this.GetOrderedCategoriesQuery ()
                               .AsNoTracking ()
                               .Select (c => new
                                             {
                                               c.Id, c.Name, c.IsAdminCategory, LinkCount = c.Links.Count,
                                             })
                               .ToListAsync ();

    return new JsonResult (categories);
  }

  /// <summary>
  ///   Returns the ordered link list used by the editor client script.
  /// </summary>
  /// <returns>A JSON payload describing all links.</returns>
  public async Task<IActionResult> OnGetLinksAsync ()
  {
    var links = await this.GetOrderedLinksQuery ()
                          .AsNoTracking ()
                          .Select (l => new
                                        {
                                          l.Id,
                                          l.CategoryId,
                                          CategoryName    = l.Category != null ? l.Category.Name : "",
                                          IsAdminCategory = l.Category != null ? l.Category.IsAdminCategory : false,
                                          l.Label,
                                          l.Description,
                                          l.IconName,
                                          l.MonitorId,
                                          l.Url,
                                        })
                          .ToListAsync ();

    return new JsonResult (links);
  }

  /// <summary>
  ///   Creates a new category from the posted editor form values.
  /// </summary>
  /// <param name="name">The category display name.</param>
  /// <param name="isAdminCategory">Whether the category belongs in the admin section.</param>
  /// <returns>A JSON payload describing the created category, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateCategoryAsync (string? name, bool isAdminCategory)
  {
    string normalizedName = (name ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
      return this.BadRequest ("Category name is required.");

    var category = new Category { Name = normalizedName, IsAdminCategory = isAdminCategory };

    this.context.Categories.Add (category);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new
                           {
                             category.Id, category.Name, category.IsAdminCategory, LinkCount = 0,
                           });
  }

  /// <summary>
  ///   Updates an existing category.
  /// </summary>
  /// <param name="id">The category identifier.</param>
  /// <param name="name">The updated category name.</param>
  /// <param name="isAdminCategory">Whether the category belongs in the admin section.</param>
  /// <returns>A JSON payload describing the updated category, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostUpdateCategoryAsync (int id, string? name, bool isAdminCategory)
  {
    string normalizedName = (name ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
      return this.BadRequest ("Category name is required.");

    Category? category = await this.context.Categories
                                   .FirstOrDefaultAsync (c => c.Id == id);

    if (category == null)
      return this.NotFound ();

    category.Name            = normalizedName;
    category.IsAdminCategory = isAdminCategory;
    await this.context.SaveChangesAsync ();

    int linkCount = await this.context.Links
                              .CountAsync (l => l.CategoryId == id);

    return new JsonResult (new
                           {
                             category.Id, category.Name, category.IsAdminCategory, LinkCount = linkCount,
                           });
  }

  /// <summary>
  ///   Deletes a category and its related links.
  /// </summary>
  /// <param name="id">The identifier of the category to remove.</param>
  /// <returns>A JSON payload describing the deleted category, or <see cref="NotFoundResult" /> when it does not exist.</returns>
  public async Task<IActionResult> OnPostDeleteCategoryAsync (int id)
  {
    Category? category = await this.context.Categories
                                   .FirstOrDefaultAsync (c => c.Id == id);

    if (category == null)
      return this.NotFound ();

    this.context.Categories.Remove (category);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id });
  }

  /// <summary>
  ///   Creates a new link from the posted editor form values.
  /// </summary>
  /// <param name="categoryId">The category that will own the link.</param>
  /// <param name="label">The display label for the link.</param>
  /// <param name="description">Optional descriptive text for the link.</param>
  /// <param name="iconName">Optional icon name to normalize and store.</param>
  /// <param name="monitorId">Optional Uptime Kuma monitor ID as a positive integer string.</param>
  /// <param name="url">The destination URL for the link.</param>
  /// <returns>A JSON payload describing the created link, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateLinkAsync (int     categoryId,
                                                          string? label,
                                                          string? description,
                                                          string? iconName,
                                                          string? monitorId,
                                                          string? url)
  {
    string normalizedLabel = (label ?? string.Empty).Trim ();
    string normalizedUrl   = (url   ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedLabel))
      return this.BadRequest ("Link label is required.");

    if (string.IsNullOrWhiteSpace (normalizedUrl))
      return this.BadRequest ("Link URL is required.");

    if (!EditorModel.IsValidWebUrl (normalizedUrl))
      return this.BadRequest ("Link URL must be a valid HTTP or HTTPS URL.");

    if (!await this.context.Categories.AnyAsync (c => c.Id == categoryId))
      return this.BadRequest ("Selected category does not exist.");

    string normalizedMonitorId = (monitorId ?? string.Empty).Trim ();

    if (!string.IsNullOrWhiteSpace (normalizedMonitorId) && !EditorModel.IsPositiveIntegerString (normalizedMonitorId))
      return this.BadRequest ("Monitor ID must be a positive integer.");

    var link = new Link
               {
                 CategoryId  = categoryId,
                 Label       = normalizedLabel,
                 Description = (description ?? string.Empty).Trim (),
                 IconName    = Link.NormalizeIconName (iconName ?? string.Empty),
                 MonitorId   = string.IsNullOrWhiteSpace (normalizedMonitorId) ? null : normalizedMonitorId,
                 Url         = normalizedUrl,
               };

    this.context.Links.Add (link);
    await this.context.SaveChangesAsync ();

    string categoryName = await this.context.Categories
                                    .Where (c => c.Id == categoryId)
                                    .Select (c => c.Name)
                                    .FirstAsync ();

    return new JsonResult (new
                           {
                             link.Id,
                             link.CategoryId,
                             CategoryName = categoryName,
                             link.Label,
                             link.Description,
                             link.IconName,
                             link.MonitorId,
                             link.Url,
                           });
  }

  /// <summary>
  ///   Updates an existing link.
  /// </summary>
  /// <param name="id">The link identifier.</param>
  /// <param name="categoryId">The updated owning category identifier.</param>
  /// <param name="label">The updated display label.</param>
  /// <param name="description">The updated descriptive text.</param>
  /// <param name="iconName">The updated icon name.</param>
  /// <param name="monitorId">The updated Uptime Kuma monitor ID.</param>
  /// <param name="url">The updated destination URL.</param>
  /// <returns>A JSON payload describing the updated link, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostUpdateLinkAsync (int     id,
                                                          int     categoryId,
                                                          string? label,
                                                          string? description,
                                                          string? iconName,
                                                          string? monitorId,
                                                          string? url)
  {
    string normalizedLabel = (label ?? string.Empty).Trim ();
    string normalizedUrl   = (url   ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedLabel))
      return this.BadRequest ("Link label is required.");

    if (string.IsNullOrWhiteSpace (normalizedUrl))
      return this.BadRequest ("Link URL is required.");

    if (!EditorModel.IsValidWebUrl (normalizedUrl))
      return this.BadRequest ("Link URL must be a valid HTTP or HTTPS URL.");

    Link? link = await this.context.Links
                           .FirstOrDefaultAsync (l => l.Id == id);

    if (link == null)
      return this.NotFound ();

    if (!await this.context.Categories.AnyAsync (c => c.Id == categoryId))
      return this.BadRequest ("Selected category does not exist.");

    string normalizedMonitorId = (monitorId ?? string.Empty).Trim ();

    if (!string.IsNullOrWhiteSpace (normalizedMonitorId) && !EditorModel.IsPositiveIntegerString (normalizedMonitorId))
      return this.BadRequest ("Monitor ID must be a positive integer.");

    link.CategoryId  = categoryId;
    link.Label       = normalizedLabel;
    link.Description = (description ?? string.Empty).Trim ();
    link.IconName    = Link.NormalizeIconName (iconName ?? string.Empty);
    link.MonitorId   = string.IsNullOrWhiteSpace (normalizedMonitorId) ? null : normalizedMonitorId;
    link.Url         = normalizedUrl;

    await this.context.SaveChangesAsync ();

    string categoryName = await this.context.Categories
                                    .Where (c => c.Id == categoryId)
                                    .Select (c => c.Name)
                                    .FirstAsync ();

    return new JsonResult (new
                           {
                             link.Id,
                             link.CategoryId,
                             CategoryName = categoryName,
                             link.Label,
                             link.Description,
                             link.IconName,
                             link.MonitorId,
                             link.Url,
                           });
  }

  /// <summary>
  ///   Deletes a link.
  /// </summary>
  /// <param name="id">The identifier of the link to remove.</param>
  /// <returns>A JSON payload describing the deleted link, or <see cref="NotFoundResult" /> when it does not exist.</returns>
  public async Task<IActionResult> OnPostDeleteLinkAsync (int id)
  {
    Link? link = await this.context.Links
                           .FirstOrDefaultAsync (l => l.Id == id);

    if (link == null)
      return this.NotFound ();

    this.context.Links.Remove (link);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id });
  }

  /// <summary>
  ///   Suggests an icon name based on the provided label when a matching selfh.st icon exists.
  /// </summary>
  /// <param name="label">The link label used to derive an icon name suggestion.</param>
  /// <returns>A JSON payload indicating whether a matching icon was found.</returns>
  public async Task<IActionResult> OnGetSuggestIconAsync (string? label)
  {
    string? suggestedIconName = await this.iconCatalog.SuggestIconNameAsync (label: label,
                                                                             cancellationToken: this.HttpContext.RequestAborted);

    return new JsonResult (new { Found = suggestedIconName != null, SuggestedIconName = suggestedIconName ?? "" });
  }

  /// <summary>
  ///   Builds the ordered category query used by the editor UI.
  /// </summary>
  /// <returns>An <see cref="IQueryable{T}" /> that orders categories by admin state and name.</returns>
  private IQueryable<Category> GetOrderedCategoriesQuery ()
    => this.context.Categories
           .Include (c => c.Links)
           .OrderBy (c => c.IsAdminCategory)
           .ThenBy (c => c.Name);

  /// <summary>
  ///   Builds the ordered link query used by the editor UI.
  /// </summary>
  /// <returns>An <see cref="IQueryable{T}" /> that orders links by category grouping and label.</returns>
  private IQueryable<Link> GetOrderedLinksQuery ()
    => this.context.Links
           .Include (l => l.Category)
           .OrderBy (l => l.Category != null ? l.Category.IsAdminCategory : false)
           .ThenBy (l => l.Category  != null ? l.Category.Name : string.Empty)
           .ThenBy (l => l.Label);

  /// <summary>
  ///   Determines whether a string is a valid absolute HTTP or HTTPS URL.
  /// </summary>
  /// <param name="url">The URL to validate.</param>
  /// <returns><c>true</c> when the URL is valid and uses HTTP or HTTPS; otherwise, <c>false</c>.</returns>
  private static bool IsValidWebUrl (string url)
  {
    if (!Uri.TryCreate (uriString: url, uriKind: UriKind.Absolute, result: out Uri? uri))
      return false;

    return (uri.Scheme == Uri.UriSchemeHttp) || (uri.Scheme == Uri.UriSchemeHttps);
  }

  /// <summary>
  ///   Determines whether a string is a valid positive integer.
  /// </summary>
  /// <param name="value">The value to validate.</param>
  /// <returns><c>true</c> when the value is a positive integer string; otherwise, <c>false</c>.</returns>
  private static bool IsPositiveIntegerString (string value)
    => int.TryParse (s: value, result: out int parsedValue) && (parsedValue > 0);
}

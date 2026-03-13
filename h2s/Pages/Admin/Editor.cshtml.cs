using System.Net;
using h2s.Data;
using h2s.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Pages.Admin;

/// <summary>
/// Page model for the admin editor used to manage categories and links.
/// </summary>
public class EditorModel : PageModel
{
  private static readonly HttpClient IconProbeClient = new ()
  {
    Timeout = TimeSpan.FromSeconds (4)
  };

  private readonly DashboardContext _context;
  private readonly IMemoryCache _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="EditorModel"/> class.
  /// </summary>
  /// <param name="context">The database context used to manage categories and links.</param>
  /// <param name="cache">The cache used to avoid repeated icon availability probes.</param>
  public EditorModel (DashboardContext context, IMemoryCache cache)
  {
    _context = context;
    _cache = cache;
  }

  /// <summary>
  /// Gets the categories shown in the editor UI.
  /// </summary>
  public List<Category> Categories { get; private set; } = new ();

  /// <summary>
  /// Gets the links shown in the editor UI.
  /// </summary>
  public List<Link> Links { get; private set; } = new ();

  /// <summary>
  /// Loads the initial category and link data required by the editor page.
  /// </summary>
  public async Task OnGetAsync ()
  {
    Categories = await GetOrderedCategoriesQuery ()
      .AsNoTracking ()
      .ToListAsync ();

    Links = await GetOrderedLinksQuery ()
      .AsNoTracking ()
      .ToListAsync ();
  }

  /// <summary>
  /// Returns the ordered category list used by the editor client script.
  /// </summary>
  /// <returns>A JSON payload describing all categories.</returns>
  public async Task<IActionResult> OnGetCategoriesAsync ()
  {
    var categories = await GetOrderedCategoriesQuery ()
      .AsNoTracking ()
      .Select (c => new
      {
        c.Id,
        c.Name,
        c.IsAdminCategory,
        LinkCount = c.Links.Count
      })
      .ToListAsync ();

    return new JsonResult (categories);
  }

  /// <summary>
  /// Returns the ordered link list used by the editor client script.
  /// </summary>
  /// <returns>A JSON payload describing all links.</returns>
  public async Task<IActionResult> OnGetLinksAsync ()
  {
    var links = await GetOrderedLinksQuery ()
      .AsNoTracking ()
      .Select (l => new
      {
        l.Id,
        l.CategoryId,
        CategoryName = l.Category != null ? l.Category.Name : "",
        IsAdminCategory = l.Category != null ? l.Category.IsAdminCategory : false,
        l.Label,
        l.Description,
        l.IconName,
        l.Url
      })
      .ToListAsync ();

    return new JsonResult (links);
  }

  /// <summary>
  /// Creates a new category from the posted editor form values.
  /// </summary>
  /// <param name="name">The category display name.</param>
  /// <param name="isAdminCategory">Whether the category belongs in the admin section.</param>
  /// <returns>A JSON payload describing the created category, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateCategoryAsync (string? name, bool isAdminCategory)
  {
    var normalizedName = (name ?? string.Empty).Trim ();
    if (string.IsNullOrWhiteSpace (normalizedName))
    {
      return BadRequest ("Category name is required.");
    }

    var category = new Category
    {
      Name = normalizedName,
      IsAdminCategory = isAdminCategory
    };

    _context.Categories.Add (category);
    await _context.SaveChangesAsync ();

    return new JsonResult (new
    {
      category.Id,
      category.Name,
      category.IsAdminCategory,
      LinkCount = 0
    });
  }

  /// <summary>
  /// Updates an existing category.
  /// </summary>
  /// <param name="id">The category identifier.</param>
  /// <param name="name">The updated category name.</param>
  /// <param name="isAdminCategory">Whether the category belongs in the admin section.</param>
  /// <returns>A JSON payload describing the updated category, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostUpdateCategoryAsync (int id, string? name, bool isAdminCategory)
  {
    var normalizedName = (name ?? string.Empty).Trim ();
    if (string.IsNullOrWhiteSpace (normalizedName))
    {
      return BadRequest ("Category name is required.");
    }

    var category = await _context.Categories
      .FirstOrDefaultAsync (c => c.Id == id);

    if (category == null)
    {
      return NotFound ();
    }

    category.Name = normalizedName;
    category.IsAdminCategory = isAdminCategory;
    await _context.SaveChangesAsync ();

    var linkCount = await _context.Links
      .CountAsync (l => l.CategoryId == id);

    return new JsonResult (new
    {
      category.Id,
      category.Name,
      category.IsAdminCategory,
      LinkCount = linkCount
    });
  }

  /// <summary>
  /// Deletes a category and its related links.
  /// </summary>
  /// <param name="id">The identifier of the category to remove.</param>
  /// <returns>A JSON payload describing the deleted category, or <see cref="NotFoundResult"/> when it does not exist.</returns>
  public async Task<IActionResult> OnPostDeleteCategoryAsync (int id)
  {
    var category = await _context.Categories
      .FirstOrDefaultAsync (c => c.Id == id);

    if (category == null)
    {
      return NotFound ();
    }

    _context.Categories.Remove (category);
    await _context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id });
  }

  /// <summary>
  /// Creates a new link from the posted editor form values.
  /// </summary>
  /// <param name="categoryId">The category that will own the link.</param>
  /// <param name="label">The display label for the link.</param>
  /// <param name="description">Optional descriptive text for the link.</param>
  /// <param name="iconName">Optional icon name to normalize and store.</param>
  /// <param name="url">The destination URL for the link.</param>
  /// <returns>A JSON payload describing the created link, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateLinkAsync (int categoryId, string? label, string? description, string? iconName, string? url)
  {
    var normalizedLabel = (label ?? string.Empty).Trim ();
    var normalizedUrl = (url ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedLabel))
    {
      return BadRequest ("Link label is required.");
    }

    if (string.IsNullOrWhiteSpace (normalizedUrl))
    {
      return BadRequest ("Link URL is required.");
    }

    if (!IsValidWebUrl (normalizedUrl))
    {
      return BadRequest ("Link URL must be a valid HTTP or HTTPS URL.");
    }

    if (!await _context.Categories.AnyAsync (c => c.Id == categoryId))
    {
      return BadRequest ("Selected category does not exist.");
    }

    var link = new Link
    {
      CategoryId = categoryId,
      Label = normalizedLabel,
      Description = (description ?? string.Empty).Trim (),
      IconName = Link.NormalizeIconName (iconName ?? string.Empty),
      Url = normalizedUrl
    };

    _context.Links.Add (link);
    await _context.SaveChangesAsync ();

    var categoryName = await _context.Categories
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
      link.Url
    });
  }

  /// <summary>
  /// Updates an existing link.
  /// </summary>
  /// <param name="id">The link identifier.</param>
  /// <param name="categoryId">The updated owning category identifier.</param>
  /// <param name="label">The updated display label.</param>
  /// <param name="description">The updated descriptive text.</param>
  /// <param name="iconName">The updated icon name.</param>
  /// <param name="url">The updated destination URL.</param>
  /// <returns>A JSON payload describing the updated link, or an error response when validation fails.</returns>
  public async Task<IActionResult> OnPostUpdateLinkAsync (int id, int categoryId, string? label, string? description, string? iconName, string? url)
  {
    var normalizedLabel = (label ?? string.Empty).Trim ();
    var normalizedUrl = (url ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedLabel))
    {
      return BadRequest ("Link label is required.");
    }

    if (string.IsNullOrWhiteSpace (normalizedUrl))
    {
      return BadRequest ("Link URL is required.");
    }

    if (!IsValidWebUrl (normalizedUrl))
    {
      return BadRequest ("Link URL must be a valid HTTP or HTTPS URL.");
    }

    var link = await _context.Links
      .FirstOrDefaultAsync (l => l.Id == id);

    if (link == null)
    {
      return NotFound ();
    }

    if (!await _context.Categories.AnyAsync (c => c.Id == categoryId))
    {
      return BadRequest ("Selected category does not exist.");
    }

    link.CategoryId = categoryId;
    link.Label = normalizedLabel;
    link.Description = (description ?? string.Empty).Trim ();
    link.IconName = Link.NormalizeIconName (iconName ?? string.Empty);
    link.Url = normalizedUrl;

    await _context.SaveChangesAsync ();

    var categoryName = await _context.Categories
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
      link.Url
    });
  }

  /// <summary>
  /// Deletes a link.
  /// </summary>
  /// <param name="id">The identifier of the link to remove.</param>
  /// <returns>A JSON payload describing the deleted link, or <see cref="NotFoundResult"/> when it does not exist.</returns>
  public async Task<IActionResult> OnPostDeleteLinkAsync (int id)
  {
    var link = await _context.Links
      .FirstOrDefaultAsync (l => l.Id == id);

    if (link == null)
    {
      return NotFound ();
    }

    _context.Links.Remove (link);
    await _context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id });
  }

  /// <summary>
  /// Suggests an icon name based on the provided label when a matching selfh.st icon exists.
  /// </summary>
  /// <param name="label">The link label used to derive an icon name suggestion.</param>
  /// <returns>A JSON payload indicating whether a matching icon was found.</returns>
  public async Task<IActionResult> OnGetSuggestIconAsync (string? label)
  {
    var suggestedIconName = Link.NormalizeIconName (label ?? string.Empty);
    if (string.IsNullOrWhiteSpace (suggestedIconName))
    {
      return new JsonResult (new { Found = false, SuggestedIconName = "" });
    }

    var cacheKey = $"icon-exists:{suggestedIconName}";
    var found = await _cache.GetOrCreateAsync (cacheKey, async entry =>
    {
      entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes (10);
      var iconUrl = Link.BuildIconUrl (suggestedIconName);
      return await IconExistsAsync (iconUrl);
    });

    return new JsonResult (new
    {
      Found = found,
      SuggestedIconName = found ? suggestedIconName : ""
    });
  }

  /// <summary>
  /// Builds the ordered category query used by the editor UI.
  /// </summary>
  /// <returns>An <see cref="IQueryable{T}"/> that orders categories by admin state and name.</returns>
  private IQueryable<Category> GetOrderedCategoriesQuery () => _context.Categories
    .Include (c => c.Links)
    .OrderBy (c => c.IsAdminCategory)
    .ThenBy (c => c.Name);

  /// <summary>
  /// Builds the ordered link query used by the editor UI.
  /// </summary>
  /// <returns>An <see cref="IQueryable{T}"/> that orders links by category grouping and label.</returns>
  private IQueryable<Link> GetOrderedLinksQuery () => _context.Links
    .Include (l => l.Category)
    .OrderBy (l => l.Category != null ? l.Category.IsAdminCategory : false)
    .ThenBy (l => l.Category != null ? l.Category.Name : string.Empty)
    .ThenBy (l => l.Label);

  /// <summary>
  /// Determines whether a string is a valid absolute HTTP or HTTPS URL.
  /// </summary>
  /// <param name="url">The URL to validate.</param>
  /// <returns><c>true</c> when the URL is valid and uses HTTP or HTTPS; otherwise, <c>false</c>.</returns>
  private static bool IsValidWebUrl (string url)
  {
    if (!Uri.TryCreate (url, UriKind.Absolute, out var uri))
    {
      return false;
    }

    return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
  }

  /// <summary>
  /// Checks whether an icon URL exists on the remote CDN.
  /// </summary>
  /// <param name="url">The icon URL to probe.</param>
  /// <returns><c>true</c> when the icon exists; otherwise, <c>false</c>.</returns>
  private static async Task<bool> IconExistsAsync (string url)
  {
    try
    {
      using var headRequest = new HttpRequestMessage (HttpMethod.Head, url);
      using var headResponse = await IconProbeClient.SendAsync (headRequest, HttpCompletionOption.ResponseHeadersRead);

      if (headResponse.IsSuccessStatusCode)
      {
        return true;
      }

      if (headResponse.StatusCode != HttpStatusCode.MethodNotAllowed)
      {
        return false;
      }

      using var getRequest = new HttpRequestMessage (HttpMethod.Get, url);
      using var getResponse = await IconProbeClient.SendAsync (getRequest, HttpCompletionOption.ResponseHeadersRead);
      return getResponse.IsSuccessStatusCode;
    }
    catch
    {
      return false;
    }
  }
}

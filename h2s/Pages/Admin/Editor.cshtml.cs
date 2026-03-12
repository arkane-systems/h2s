using System.Net;
using h2s.Data;
using h2s.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace h2s.Pages.Admin;

public class EditorModel : PageModel
{
  private static readonly HttpClient IconProbeClient = new ()
  {
    Timeout = TimeSpan.FromSeconds (4)
  };

  private readonly DashboardContext _context;
  private readonly IMemoryCache _cache;

  public EditorModel (DashboardContext context, IMemoryCache cache)
  {
    _context = context;
    _cache = cache;
  }

  public List<Category> Categories { get; private set; } = new ();
  public List<Link> Links { get; private set; } = new ();

  public async Task OnGetAsync ()
  {
    Categories = await GetOrderedCategoriesQuery ()
      .AsNoTracking ()
      .ToListAsync ();

    Links = await GetOrderedLinksQuery ()
      .AsNoTracking ()
      .ToListAsync ();
  }

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

  private IQueryable<Category> GetOrderedCategoriesQuery () => _context.Categories
    .Include (c => c.Links)
    .OrderBy (c => c.IsAdminCategory)
    .ThenBy (c => c.Name);

  private IQueryable<Link> GetOrderedLinksQuery () => _context.Links
    .Include (l => l.Category)
    .OrderBy (l => l.Category != null ? l.Category.IsAdminCategory : false)
    .ThenBy (l => l.Category != null ? l.Category.Name : string.Empty)
    .ThenBy (l => l.Label);

  private static bool IsValidWebUrl (string url)
  {
    if (!Uri.TryCreate (url, UriKind.Absolute, out var uri))
    {
      return false;
    }

    return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
  }

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

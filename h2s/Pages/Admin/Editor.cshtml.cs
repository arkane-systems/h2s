using System.Text.Json;
using h2s.Data;
using h2s.Models;
using h2s.Services;
using h2s.Validation;
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
  private readonly PageMetadataReader       pageMetadataReader;
  private readonly DashboardSettingsService settingsService;
  private readonly UptimeKumaClient         uptimeKumaClient;

  /// <summary>
  ///   Initializes a new instance of the <see cref="EditorModel" /> class.
  /// </summary>
  /// <param name="context">The database context used to manage categories and links.</param>
  /// <param name="iconCatalog">The catalog used to suggest icons for link labels.</param>
  /// <param name="settingsService">The service used to load dashboard settings.</param>
  /// <param name="pageMetadataReader">The reader used to suggest a label from a link's page.</param>
  /// <param name="uptimeKumaClient">The client used to list the monitors on the Uptime Kuma status page.</param>
  public EditorModel (DashboardContext         context,
                      IconCatalog              iconCatalog,
                      DashboardSettingsService settingsService,
                      PageMetadataReader       pageMetadataReader,
                      UptimeKumaClient         uptimeKumaClient)
  {
    this.context            = context;
    this.iconCatalog        = iconCatalog;
    this.settingsService    = settingsService;
    this.pageMetadataReader = pageMetadataReader;
    this.uptimeKumaClient   = uptimeKumaClient;
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
                                          l.IconUrl,
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
  /// <returns>A JSON payload describing the created category, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateCategoryAsync (string? name, bool isAdminCategory)
  {
    string normalizedName = (name ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
      return this.BadRequestProblem ("Category name is required.");

    var category = new Category
                   {
                     Name            = normalizedName,
                     IsAdminCategory = isAdminCategory,
                     SortOrder       = await this.GetNextCategorySortOrderAsync (isAdminCategory),
                   };

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
  /// <returns>A JSON payload describing the updated category, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostUpdateCategoryAsync (int id, string? name, bool isAdminCategory)
  {
    string normalizedName = (name ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (normalizedName))
      return this.BadRequestProblem ("Category name is required.");

    Category? category = await this.context.Categories
                                   .FirstOrDefaultAsync (c => c.Id == id);

    if (category == null)
      return this.NotFoundProblem ("Category not found.");

    // A category that changes section goes to the end of its new section.
    if (category.IsAdminCategory != isAdminCategory)
      category.SortOrder = await this.GetNextCategorySortOrderAsync (isAdminCategory);

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
  /// <returns>
  ///   A JSON payload with the deleted category's ID and a snapshot that <see cref="OnPostRestoreCategoryAsync" /> can
  ///   recreate it from, or a 404 problem response when it does not exist.
  /// </returns>
  public async Task<IActionResult> OnPostDeleteCategoryAsync (int id)
  {
    Category? category = await this.context.Categories
                                   .Include (c => c.Links)
                                   .FirstOrDefaultAsync (c => c.Id == id);

    if (category == null)
      return this.NotFoundProblem ("Category not found.");

    CategorySnapshot snapshot = CategorySnapshot.From (category);

    this.context.Categories.Remove (category);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id, Restore = snapshot });
  }

  /// <summary>
  ///   Creates a new link from the posted editor form values.
  /// </summary>
  /// <param name="categoryId">The category that will own the link.</param>
  /// <param name="label">The display label for the link.</param>
  /// <param name="description">Optional descriptive text for the link.</param>
  /// <param name="iconName">Optional icon name to normalize and store.</param>
  /// <param name="iconUrl">Optional absolute image URL used as the icon when there is no icon name.</param>
  /// <param name="monitorId">Optional Uptime Kuma monitor ID; blank for none.</param>
  /// <param name="url">The destination URL for the link.</param>
  /// <returns>A JSON payload describing the created link, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostCreateLinkAsync (int     categoryId,
                                                          string? label,
                                                          string? description,
                                                          string? iconName,
                                                          string? iconUrl,
                                                          string? monitorId,
                                                          string? url)
  {
    string normalizedLabel   = (label   ?? string.Empty).Trim ();
    string normalizedUrl     = (url     ?? string.Empty).Trim ();
    string normalizedIconUrl = (iconUrl ?? string.Empty).Trim ();

    if (EditorModel.ValidateLink (label: normalizedLabel, url: normalizedUrl, iconUrl: normalizedIconUrl) is { } invalid)
      return this.BadRequestProblem (invalid);

    if (!await this.context.Categories.AnyAsync (c => c.Id == categoryId))
      return this.BadRequestProblem ("Selected category does not exist.");

    if (!MonitorIdParser.TryParseOptional (value: monitorId, monitorId: out int? parsedMonitorId))
      return this.BadRequestProblem ("Monitor ID must be a positive integer.");

    var link = new Link
               {
                 CategoryId  = categoryId,
                 Label       = normalizedLabel,
                 Description = (description ?? string.Empty).Trim (),
                 IconName    = Link.NormalizeIconName (iconName ?? string.Empty),
                 IconUrl     = normalizedIconUrl,
                 MonitorId   = parsedMonitorId,
                 Url         = normalizedUrl,
                 SortOrder   = await this.GetNextLinkSortOrderAsync (categoryId),
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
                             link.IconUrl,
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
  /// <param name="iconUrl">
  ///   The updated icon image URL; blank to remove it. When the field is not posted at all, the stored URL is kept.
  /// </param>
  /// <param name="monitorId">
  ///   The updated Uptime Kuma monitor ID; blank to remove it. When the field is not posted at all, the stored monitor ID
  ///   is kept.
  /// </param>
  /// <param name="url">The updated destination URL.</param>
  /// <returns>A JSON payload describing the updated link, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostUpdateLinkAsync (int     id,
                                                          int     categoryId,
                                                          string? label,
                                                          string? description,
                                                          string? iconName,
                                                          string? iconUrl,
                                                          string? monitorId,
                                                          string? url)
  {
    string normalizedLabel   = (label   ?? string.Empty).Trim ();
    string normalizedUrl     = (url     ?? string.Empty).Trim ();
    string normalizedIconUrl = (iconUrl ?? string.Empty).Trim ();

    if (EditorModel.ValidateLink (label: normalizedLabel, url: normalizedUrl, iconUrl: normalizedIconUrl) is { } invalid)
      return this.BadRequestProblem (invalid);

    Link? link = await this.context.Links
                           .FirstOrDefaultAsync (l => l.Id == id);

    if (link == null)
      return this.NotFoundProblem ("Link not found.");

    if (!await this.context.Categories.AnyAsync (c => c.Id == categoryId))
      return this.BadRequestProblem ("Selected category does not exist.");

    if (!MonitorIdParser.TryParseOptional (value: monitorId, monitorId: out int? parsedMonitorId))
      return this.BadRequestProblem ("Monitor ID must be a positive integer.");

    // A link that moves to another category goes to the end of it.
    if (link.CategoryId != categoryId)
      link.SortOrder = await this.GetNextLinkSortOrderAsync (categoryId);

    link.CategoryId  = categoryId;
    link.Label       = normalizedLabel;
    link.Description = (description ?? string.Empty).Trim ();
    link.IconName    = Link.NormalizeIconName (iconName ?? string.Empty);
    link.Url         = normalizedUrl;

    // Without Uptime Kuma the editor has no monitor ID field and omits it, which leaves the stored monitor ID alone;
    // only a posted (possibly blank) value replaces it.
    if (this.Request.Form.ContainsKey (nameof (monitorId)))
      link.MonitorId = parsedMonitorId;

    // Likewise the older Editor page has no icon URL field.
    if (this.Request.Form.ContainsKey (nameof (iconUrl)))
      link.IconUrl = normalizedIconUrl;

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
                             link.IconUrl,
                             link.MonitorId,
                             link.Url,
                           });
  }

  /// <summary>
  ///   Deletes a link.
  /// </summary>
  /// <param name="id">The identifier of the link to remove.</param>
  /// <returns>
  ///   A JSON payload with the deleted link's ID and a snapshot that <see cref="OnPostRestoreLinkAsync" /> can recreate it
  ///   from, or a 404 problem response when it does not exist.
  /// </returns>
  public async Task<IActionResult> OnPostDeleteLinkAsync (int id)
  {
    Link? link = await this.context.Links
                           .FirstOrDefaultAsync (l => l.Id == id);

    if (link == null)
      return this.NotFoundProblem ("Link not found.");

    LinkSnapshot snapshot = LinkSnapshot.From (link);

    this.context.Links.Remove (link);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new { DeletedId = id, Restore = snapshot });
  }

  /// <summary>
  ///   Recreates a deleted link from the snapshot its delete returned, at its old position in its category.
  /// </summary>
  /// <param name="snapshot">The <see cref="LinkSnapshot" />, as JSON.</param>
  /// <returns>
  ///   A JSON payload describing the recreated link, a 400 problem response when the snapshot is invalid, or a 404 problem
  ///   response when its category no longer exists.
  /// </returns>
  public async Task<IActionResult> OnPostRestoreLinkAsync (string? snapshot)
  {
    var restore = EditorModel.ReadSnapshot<LinkSnapshot> (snapshot);

    if (restore == null)
      return this.BadRequestProblem ("The link to restore is missing or malformed.");

    if (!await this.context.Categories.AnyAsync (c => c.Id == restore.CategoryId))
      return this.NotFoundProblem ("The link's category no longer exists.");

    if (EditorModel.ValidateSnapshot (restore) is { } invalid)
      return this.BadRequestProblem (invalid);

    int count    = await this.context.Links.CountAsync (l => l.CategoryId == restore.CategoryId);
    int position = Math.Clamp (value: restore.SortOrder, min: 0, max: count);

    _ = await this.context.Links
                  .Where (l => (l.CategoryId == restore.CategoryId) && (l.SortOrder >= position))
                  .ExecuteUpdateAsync (s => s.SetProperty (propertyExpression: l => l.SortOrder,
                                                           valueExpression: l => l.SortOrder + 1));

    Link link = EditorModel.CreateLink (snapshot: restore, sortOrder: position);
    link.CategoryId = restore.CategoryId;

    this.context.Links.Add (link);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new
                           {
                             link.Id,
                             link.CategoryId,
                             link.Label,
                             link.Description,
                             link.IconName,
                             link.IconUrl,
                             link.MonitorId,
                             link.Url,
                             link.SortOrder,
                           });
  }

  /// <summary>
  ///   Recreates a deleted category and its links from the snapshot its delete returned, at its old position in its
  ///   section.
  /// </summary>
  /// <param name="snapshot">The <see cref="CategorySnapshot" />, as JSON.</param>
  /// <returns>A JSON payload with the recreated category's ID, or a 400 problem response when the snapshot is invalid.</returns>
  public async Task<IActionResult> OnPostRestoreCategoryAsync (string? snapshot)
  {
    var restore = EditorModel.ReadSnapshot<CategorySnapshot> (snapshot);

    if (restore == null)
      return this.BadRequestProblem ("The category to restore is missing or malformed.");

    string name = (restore.Name ?? string.Empty).Trim ();

    if (string.IsNullOrWhiteSpace (name))
      return this.BadRequestProblem ("Category name is required.");

    List<LinkSnapshot> links = restore.Links ?? [];

    if (links.Select (EditorModel.ValidateSnapshot).FirstOrDefault (e => e != null) is { } invalid)
      return this.BadRequestProblem (invalid);

    int count    = await this.context.Categories.CountAsync (c => c.IsAdminCategory == restore.IsAdminCategory);
    int position = Math.Clamp (value: restore.SortOrder, min: 0, max: count);

    _ = await this.context.Categories
                  .Where (c => (c.IsAdminCategory == restore.IsAdminCategory) && (c.SortOrder >= position))
                  .ExecuteUpdateAsync (s => s.SetProperty (propertyExpression: c => c.SortOrder,
                                                           valueExpression: c => c.SortOrder + 1));

    var category = new Category
                   {
                     Name            = name,
                     IsAdminCategory = restore.IsAdminCategory,
                     SortOrder       = position,
                     Links           = [.. links.Select ((l, i) => EditorModel.CreateLink (snapshot: l, sortOrder: i))],
                   };

    this.context.Categories.Add (category);
    await this.context.SaveChangesAsync ();

    return new JsonResult (new { category.Id });
  }

  /// <summary>
  ///   Returns one link, for the link dialog to edit.
  /// </summary>
  /// <param name="id">The link identifier.</param>
  /// <returns>A JSON payload describing the link, or a 404 problem response when it does not exist.</returns>
  public async Task<IActionResult> OnGetLinkAsync (int id)
  {
    var link = await this.context.Links
                         .AsNoTracking ()
                         .Where (l => l.Id == id)
                         .Select (l => new
                                       {
                                         l.Id,
                                         l.CategoryId,
                                         l.Label,
                                         l.Description,
                                         l.IconName,
                                         l.IconUrl,
                                         l.MonitorId,
                                         l.Url,
                                       })
                         .FirstOrDefaultAsync ();

    return link == null ? this.NotFoundProblem ("Link not found.") : new JsonResult (link);
  }

  /// <summary>
  ///   Suggests a label and an icon for a link from its URL, following the URL to the page that describes the app. The
  ///   label is the page title, or else the app's name; the icon matches the label, the app's names, the title or the
  ///   host name.
  /// </summary>
  /// <param name="url">The link URL.</param>
  /// <param name="label">The label entered so far, if any; it is tried first when suggesting an icon.</param>
  /// <returns>
  ///   A JSON payload with the suggested label, the selfh.st icon name and the site's own icon URL, any of which may be
  ///   <c>null</c>.
  /// </returns>
  public async Task<IActionResult> OnGetSuggestLinkAsync (string? url, string? label)
  {
    CancellationToken cancellationToken = this.HttpContext.RequestAborted;
    PageMetadata      metadata = await this.pageMetadataReader.ReadAsync (url: url, cancellationToken: cancellationToken);
    List<string?>     names = [label, .. metadata.AppNames, metadata.Title, .. PageMetadataReader.GetTitleParts (metadata.Title)];

    // Whole names first, then the host, then each name's first word as a last resort.
    string? iconName =
      await this.iconCatalog.SuggestIconNameAsync (candidates:
                                                   [
                                                     .. names,
                                                     .. IconCatalog.GetHostCandidates (url),
                                                     .. IconCatalog.GetFirstWords (names),
                                                   ],
                                                   cancellationToken: cancellationToken);

    return new JsonResult (new { Label = metadata.SuggestedLabel, IconName = iconName, metadata.IconUrl });
  }

  /// <summary>
  ///   Lists the monitors on the configured Uptime Kuma status page, for the link dialog's monitor picker.
  /// </summary>
  /// <returns>
  ///   A JSON payload with the monitor groups, and a message instead when they can't be listed: Kuma or its status page
  ///   isn't configured, or Kuma can't be read. The dialog then falls back to entering an ID.
  /// </returns>
  public async Task<IActionResult> OnGetKumaMonitorsAsync ()
  {
    DashboardSettings settings = await this.settingsService.GetSettingsAsync ();

    if (string.IsNullOrWhiteSpace (settings.UptimeKumaServerUrl))
      return new JsonResult (KumaMonitorList.Failed ("Uptime Kuma isn't configured."));

    if (string.IsNullOrWhiteSpace (settings.UptimeKumaStatusPageSlug))
      return new JsonResult (KumaMonitorList.Failed ("Set a status page slug in Settings to pick monitors from it."));

    return new JsonResult (await this.uptimeKumaClient.GetStatusPageMonitorsAsync (serverUrl: settings.UptimeKumaServerUrl,
                                                                                   slug: settings.UptimeKumaStatusPageSlug,
                                                                                   cancellationToken: this.HttpContext
                                                                                                          .RequestAborted));
  }

  /// <summary>
  ///   Searches the selfh.st icons for the link dialog's icon picker.
  /// </summary>
  /// <param name="q">The search text.</param>
  /// <returns>A JSON array of up to 48 icons, each with its name, reference and thumbnail URL.</returns>
  public async Task<IActionResult> OnGetSearchIconsAsync (string? q)
  {
    IReadOnlyList<IconInfo> icons = await this.iconCatalog.SearchAsync (query: q,
                                                                        limit: 48,
                                                                        cancellationToken: this.HttpContext.RequestAborted);

    return new JsonResult (icons.Select (i => new { i.Reference, i.Name, Url = Link.BuildIconUrl (i.Reference) }));
  }

  /// <summary>
  ///   Renders the tile a link would have on the dashboard, for the link dialog's live preview. Nothing is saved.
  /// </summary>
  /// <param name="label">The link label.</param>
  /// <param name="url">The link URL.</param>
  /// <param name="description">The link description.</param>
  /// <param name="iconName">The selfh.st icon name.</param>
  /// <param name="iconUrl">The icon image URL; ignored when not an HTTP or HTTPS URL.</param>
  /// <param name="monitorId">The Uptime Kuma monitor ID; ignored when blank or invalid.</param>
  /// <returns>The tile's HTML.</returns>
  public async Task<IActionResult> OnGetTilePreviewAsync (string? label,
                                                          string? url,
                                                          string? description,
                                                          string? iconName,
                                                          string? iconUrl,
                                                          string? monitorId)
  {
    _ = MonitorIdParser.TryParseOptional (value: monitorId, monitorId: out int? parsedMonitorId);

    var link = new Link
               {
                 Label       = (label       ?? string.Empty).Trim (),
                 Url         = (url         ?? string.Empty).Trim (),
                 Description = (description ?? string.Empty).Trim (),
                 IconName    = Link.NormalizeIconName (iconName ?? string.Empty),
                 IconUrl     = EditorModel.IsValidWebUrl ((iconUrl ?? string.Empty).Trim ()) ? iconUrl!.Trim () : "",
                 MonitorId   = parsedMonitorId,
               };

    return this.Partial (viewName: "_LinkTile", model: await this.CreateTileAsync (link));
  }

  /// <summary>
  ///   Renders a saved link as dashboard edit mode shows it, so the page can show a new or changed link without reloading.
  /// </summary>
  /// <param name="id">The link identifier.</param>
  /// <returns>The link slot's HTML, or a 404 problem response when the link does not exist.</returns>
  public async Task<IActionResult> OnGetLinkSlotAsync (int id)
  {
    Link? link = await this.context.Links
                           .AsNoTracking ()
                           .FirstOrDefaultAsync (l => l.Id == id);

    return link == null
             ? this.NotFoundProblem ("Link not found.")
             : this.Partial (viewName: "_EditableLinkSlot", model: await this.CreateTileAsync (link));
  }

  /// <summary>
  ///   Reorders the categories of one dashboard section (regular or admin).
  /// </summary>
  /// <param name="orderedIds">The IDs of every category in the section, in the desired order.</param>
  /// <returns>A JSON payload confirming success, or a 400 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostReorderCategoriesAsync (string[]? orderedIds)
  {
    if (!OrderedIdParser.TryParse (values: orderedIds, ids: out List<int>? ids, error: out string? error))
      return this.BadRequestProblem (error);

    List<Category> categories = await this.context.Categories
                                          .Where (c => ids.Contains (c.Id))
                                          .ToListAsync ();

    if (categories.Count != ids.Count)
      return this.BadRequestProblem ("One or more categories do not exist.");

    bool isAdminSection = categories[0].IsAdminCategory;

    if (categories.Any (c => c.IsAdminCategory != isAdminSection))
      return this.BadRequestProblem ("Regular and admin categories are ordered separately.");

    if (await this.context.Categories.CountAsync (c => c.IsAdminCategory == isAdminSection) != ids.Count)
      return this.BadRequestProblem ("The order must include every category in the section.");

    Dictionary<int, Category> categoriesById = categories.ToDictionary (c => c.Id);

    for (var i = 0; i < ids.Count; i++)
      categoriesById[ids[i]].SortOrder = i;

    await this.context.SaveChangesAsync ();

    return new JsonResult (new { Success = true });
  }

  /// <summary>
  ///   Reorders the links of one category, moving any listed links that belong to other categories into it.
  /// </summary>
  /// <param name="categoryId">The category whose links are ordered.</param>
  /// <param name="orderedIds">
  ///   The IDs of every link in the category, plus any links being moved into it, in the desired order.
  /// </param>
  /// <returns>A JSON payload confirming success, or a 400 or 404 problem response when validation fails.</returns>
  public async Task<IActionResult> OnPostReorderLinksAsync (int categoryId, string[]? orderedIds)
  {
    if (!OrderedIdParser.TryParse (values: orderedIds, ids: out List<int>? ids, error: out string? error))
      return this.BadRequestProblem (error);

    if (!await this.context.Categories.AnyAsync (c => c.Id == categoryId))
      return this.NotFoundProblem ("Category not found.");

    List<Link> links = await this.context.Links
                                 .Where (l => ids.Contains (l.Id))
                                 .ToListAsync ();

    if (links.Count != ids.Count)
      return this.BadRequestProblem ("One or more links do not exist.");

    if (await this.context.Links.AnyAsync (l => (l.CategoryId == categoryId) && !ids.Contains (l.Id)))
      return this.BadRequestProblem ("The order must include every link in the category.");

    List<int> sourceCategoryIds = links.Where (l => l.CategoryId != categoryId)
                                       .Select (l => l.CategoryId)
                                       .Distinct ()
                                       .ToList ();

    Dictionary<int, Link> linksById = links.ToDictionary (l => l.Id);

    for (var i = 0; i < ids.Count; i++)
    {
      Link link = linksById[ids[i]];
      link.CategoryId = categoryId;
      link.SortOrder  = i;
    }

    // Close the gaps that moved links leave in the categories they came from.
    if (sourceCategoryIds.Count > 0)
    {
      List<Link> remaining = await this.context.Links
                                       .Where (l => sourceCategoryIds.Contains (l.CategoryId) && !ids.Contains (l.Id))
                                       .OrderBy (l => l.SortOrder)
                                       .ThenBy (l => l.Label)
                                       .ToListAsync ();

      foreach (IGrouping<int, Link> category in remaining.GroupBy (l => l.CategoryId))
      {
        var position = 0;

        foreach (Link link in category)
          link.SortOrder = position++;
      }
    }

    await this.context.SaveChangesAsync ();

    return new JsonResult (new { Success = true });
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
  /// <returns>An <see cref="IQueryable{T}" /> that orders categories by section, then as on the dashboard.</returns>
  private IQueryable<Category> GetOrderedCategoriesQuery ()
    => this.context.Categories
           .Include (c => c.Links)
           .OrderBy (c => c.IsAdminCategory)
           .ThenBy (c => c.SortOrder)
           .ThenBy (c => c.Name);

  /// <summary>
  ///   Builds the ordered link query used by the editor UI.
  /// </summary>
  /// <returns>An <see cref="IQueryable{T}" /> that orders links by category, then as on the dashboard.</returns>
  private IQueryable<Link> GetOrderedLinksQuery ()
    => this.context.Links
           .Include (l => l.Category)
           .OrderBy (l => l.Category != null ? l.Category.IsAdminCategory : false)
           .ThenBy (l => l.Category  != null ? l.Category.SortOrder : 0)
           .ThenBy (l => l.Category  != null ? l.Category.Name : string.Empty)
           .ThenBy (l => l.SortOrder)
           .ThenBy (l => l.Label);

  /// <summary>
  ///   Gets the sort order that places a category at the end of its section.
  /// </summary>
  /// <param name="isAdminCategory">Whether the section is the admin section.</param>
  /// <returns>One more than the section's highest sort order, or 0 for an empty section.</returns>
  private async Task<int> GetNextCategorySortOrderAsync (bool isAdminCategory)
    => (await this.context.Categories
                  .Where (c => c.IsAdminCategory == isAdminCategory)
                  .Select (c => (int?)c.SortOrder)
                  .MaxAsync () ??
        -1) +
       1;

  /// <summary>
  ///   Gets the sort order that places a link at the end of a category.
  /// </summary>
  /// <param name="categoryId">The category.</param>
  /// <returns>One more than the category's highest sort order, or 0 for an empty category.</returns>
  private async Task<int> GetNextLinkSortOrderAsync (int categoryId)
    => (await this.context.Links
                  .Where (l => l.CategoryId == categoryId)
                  .Select (l => (int?)l.SortOrder)
                  .MaxAsync () ??
        -1) +
       1;

  /// <summary>
  ///   Builds a link's tile with the current dashboard settings.
  /// </summary>
  /// <param name="link">The link.</param>
  /// <returns>The tile.</returns>
  private async Task<LinkTile> CreateTileAsync (Link link)
    => new LinkTileFactory (await this.settingsService.GetSettingsAsync ()).Create (link);

  /// <summary>
  ///   Checks a link's required fields.
  /// </summary>
  /// <param name="label">The trimmed label.</param>
  /// <param name="url">The trimmed URL.</param>
  /// <param name="iconUrl">The trimmed icon image URL; empty for none.</param>
  /// <returns>The problem with the link, or <c>null</c> when it is valid.</returns>
  private static string? ValidateLink (string label, string url, string iconUrl)
  {
    if (string.IsNullOrWhiteSpace (label))
      return "Link label is required.";

    if (string.IsNullOrWhiteSpace (url))
      return "Link URL is required.";

    if (!EditorModel.IsValidWebUrl (url))
      return "Link URL must be a valid HTTP or HTTPS URL.";

    return (iconUrl.Length == 0) || EditorModel.IsValidWebUrl (iconUrl) ? null : "Icon URL must be a valid HTTP or HTTPS URL.";
  }

  /// <summary>
  ///   Checks a link snapshot before restoring it.
  /// </summary>
  /// <param name="snapshot">The snapshot.</param>
  /// <returns>The problem with the snapshot, or <c>null</c> when it is valid.</returns>
  private static string? ValidateSnapshot (LinkSnapshot snapshot)
  {
    if (snapshot.MonitorId is <= 0)
      return "Monitor ID must be a positive integer.";

    return EditorModel.ValidateLink (label: (snapshot.Label     ?? string.Empty).Trim (),
                                     url: (snapshot.Url         ?? string.Empty).Trim (),
                                     iconUrl: (snapshot.IconUrl ?? string.Empty).Trim ());
  }

  /// <summary>
  ///   Creates a link from a validated snapshot. The caller sets its category.
  /// </summary>
  /// <param name="snapshot">The snapshot.</param>
  /// <param name="sortOrder">The link's position within its category.</param>
  /// <returns>The link.</returns>
  private static Link CreateLink (LinkSnapshot snapshot, int sortOrder)
    => new ()
       {
         Label       = (snapshot.Label       ?? string.Empty).Trim (),
         Description = (snapshot.Description ?? string.Empty).Trim (),
         IconName    = Link.NormalizeIconName (snapshot.IconName ?? string.Empty),
         IconUrl     = (snapshot.IconUrl ?? string.Empty).Trim (),
         MonitorId   = snapshot.MonitorId,
         Url         = (snapshot.Url ?? string.Empty).Trim (),
         SortOrder   = sortOrder,
       };

  /// <summary>
  ///   Reads a snapshot posted back by Undo.
  /// </summary>
  /// <typeparam name="T">The snapshot type.</typeparam>
  /// <param name="json">The snapshot, as JSON.</param>
  /// <returns>The snapshot, or <c>null</c> when it is missing or malformed.</returns>
  private static T? ReadSnapshot<T> (string? json)
    where T : class
  {
    if (string.IsNullOrWhiteSpace (json))
      return null;

    try
    {
      return JsonSerializer.Deserialize<T> (json: json, options: JsonSerializerOptions.Web);
    }
    catch (JsonException)
    {
      return null;
    }
  }

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
}

using h2s.Data;
using h2s.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace h2s.Pages;

public class IndexModel : PageModel
{
  private readonly DashboardContext _context;
  public List<Category> Categories { get; set; } = new ();

  public IndexModel (DashboardContext context) => this._context = context;

  public void OnGet () => this.Categories = this._context.Categories
        .Include (c => c.Links)
        .OrderBy (c => c.SortOrder)
        .ThenBy (c => c.Name)
        .ToList ();
}

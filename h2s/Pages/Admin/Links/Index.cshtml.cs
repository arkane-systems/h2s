using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using h2s.Data;
using h2s.Models;

namespace h2s.Pages.Admin.Links
{
    public class IndexModel : PageModel
    {
        private readonly h2s.Data.DashboardContext _context;

        public IndexModel(h2s.Data.DashboardContext context)
        {
            _context = context;
        }

        public IList<Link> Link { get;set; } = default!;

        public async Task OnGetAsync()
        {
            Link = await _context.Links
                .Include(l => l.Category).ToListAsync();
        }
    }
}

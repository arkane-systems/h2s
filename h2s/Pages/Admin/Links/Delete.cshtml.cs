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
    public class DeleteModel : PageModel
    {
        private readonly h2s.Data.DashboardContext _context;

        public DeleteModel(h2s.Data.DashboardContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Link Link { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var link = await _context.Links.FirstOrDefaultAsync(m => m.Id == id);

            if (link is not null)
            {
                Link = link;

                return Page();
            }

            return NotFound();
        }

        public async Task<IActionResult> OnPostAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var link = await _context.Links.FindAsync(id);
            if (link != null)
            {
                Link = link;
                _context.Links.Remove(Link);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }
    }
}

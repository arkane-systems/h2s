namespace h2s.Data;

using h2s.Models;
using Microsoft.EntityFrameworkCore;

public class DashboardContext : DbContext
{
  public DashboardContext (DbContextOptions<DashboardContext> options)
      : base (options)
  {
  }

  public DbSet<Category> Categories => this.Set<Category> ();
  public DbSet<Link> Links => this.Set<Link> ();
}

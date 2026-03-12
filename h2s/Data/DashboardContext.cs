using h2s.Models;
using Microsoft.EntityFrameworkCore;

namespace h2s.Data;

public class DashboardContext (DbContextOptions<DashboardContext> options) : DbContext (options)
{
  public DbSet<Category> Categories => this.Set<Category> ();
  public DbSet<Link> Links => this.Set<Link> ();
  public DbSet<DashboardSettings> DashboardSettings => this.Set<DashboardSettings> ();

  protected override void OnModelCreating (ModelBuilder modelBuilder)
  {
    base.OnModelCreating (modelBuilder);

    // Ensure only one DashboardSettings record can exist
    _ = modelBuilder.Entity<DashboardSettings> ()
      .ToTable (t => t.HasCheckConstraint ("CK_DashboardSettings_SingleRow", "Id = 1"));
  }
}

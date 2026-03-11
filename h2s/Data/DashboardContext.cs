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
  public DbSet<DashboardSettings> DashboardSettings => this.Set<DashboardSettings> ();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    // Ensure only one DashboardSettings record can exist
    modelBuilder.Entity<DashboardSettings> ()
      .HasCheckConstraint ("CK_DashboardSettings_SingleRow", "Id = 1");
  }
}

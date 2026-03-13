using h2s.Models;
using Microsoft.EntityFrameworkCore;

namespace h2s.Data;

/// <summary>
/// Entity Framework Core database context for the dashboard application.
/// </summary>
/// <param name="options">The configured database context options.</param>
public class DashboardContext (DbContextOptions<DashboardContext> options) : DbContext (options)
{
  /// <summary>
  /// Gets the set of dashboard categories.
  /// </summary>
  public DbSet<Category> Categories => this.Set<Category> ();

  /// <summary>
  /// Gets the set of links shown within dashboard categories.
  /// </summary>
  public DbSet<Link> Links => this.Set<Link> ();

  /// <summary>
  /// Gets the singleton dashboard settings record.
  /// </summary>
  public DbSet<DashboardSettings> DashboardSettings => this.Set<DashboardSettings> ();

  /// <summary>
  /// Configures entity mappings and constraints for the dashboard schema.
  /// </summary>
  /// <param name="modelBuilder">The model builder used to configure EF Core entities.</param>
  protected override void OnModelCreating (ModelBuilder modelBuilder)
  {
    base.OnModelCreating (modelBuilder);

    // Ensure only one DashboardSettings record can exist.
    _ = modelBuilder.Entity<DashboardSettings> ()
      .ToTable (t => t.HasCheckConstraint ("CK_DashboardSettings_SingleRow", "Id = 1"));
  }
}

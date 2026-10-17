using h2s.Models;
using Microsoft.EntityFrameworkCore;

namespace h2s.Data;

/// <summary>
///   Entity Framework Core database context for the dashboard application.
/// </summary>
/// <param name="options">The configured database context options.</param>
public class DashboardContext (DbContextOptions<DashboardContext> options) : DbContext (options)
{
  /// <summary>
  ///   Gets the set of dashboard categories.
  /// </summary>
  public DbSet<Category> Categories => this.Set<Category> ();

  /// <summary>
  ///   Gets the set of links shown within dashboard categories.
  /// </summary>
  public DbSet<Link> Links => this.Set<Link> ();

  /// <summary>
  ///   Gets the singleton dashboard settings record.
  /// </summary>
  public DbSet<DashboardSettings> DashboardSettings => this.Set<DashboardSettings> ();

  /// <summary>
  ///   Gets the set of full-width panels shown above and below the categories.
  /// </summary>
  public DbSet<Panel> Panels => this.Set<Panel> ();

  /// <summary>
  ///   Gets the set of connections to other services, each holding one integration's settings.
  /// </summary>
  public DbSet<Connection> Connections => this.Set<Connection> ();

  /// <summary>
  ///   Gets the set of widgets on links' tiles.
  /// </summary>
  public DbSet<Widget> Widgets => this.Set<Widget> ();

  /// <summary>
  ///   Gets the set of web search engines.
  /// </summary>
  public DbSet<SearchEngine> SearchEngines => this.Set<SearchEngine> ();

  /// <summary>
  ///   Configures entity mappings and constraints for the dashboard schema.
  /// </summary>
  /// <param name="modelBuilder">The model builder used to configure EF Core entities.</param>
  protected override void OnModelCreating (ModelBuilder modelBuilder)
  {
    base.OnModelCreating (modelBuilder);

    // Ensure only one DashboardSettings record can exist.
    _ = modelBuilder.Entity<DashboardSettings> ()
                    .ToTable (t => t.HasCheckConstraint (name: "CK_DashboardSettings_SingleRow", sql: "Id = 1"));

    // A widget belongs to exactly one link or panel, and goes with it.
    _ = modelBuilder.Entity<Widget> (widget =>
                                     {
                                       _ = widget.ToTable (t => t.HasCheckConstraint (name: "CK_Widgets_OneOwner",
                                                                                      sql:
                                                                                      "(\"LinkId\" IS NULL) <> (\"PanelId\" IS NULL)"));
                                       _ = widget.HasOne (w => w.Link)
                                                 .WithMany (l => l.Widgets)
                                                 .HasForeignKey (w => w.LinkId)
                                                 .OnDelete (DeleteBehavior.Cascade);
                                       _ = widget.HasOne (w => w.Panel)
                                                 .WithMany (p => p.Widgets)
                                                 .HasForeignKey (w => w.PanelId)
                                                 .OnDelete (DeleteBehavior.Cascade);
                                     });
  }
}

namespace h2s.Models;

public class DashboardSettings
{
  public int Id { get; set; }
  public string Title { get; set; } = "";
  public string Motto { get; set; } = "";
  public string LocalDomains { get; set; } = "";
  public ColorMode ColorMode { get; set; } = ColorMode.Auto;
}

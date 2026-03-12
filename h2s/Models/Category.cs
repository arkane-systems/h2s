namespace h2s.Models;

public class Category
{
  public int Id { get; set; }
  public string Name { get; set; } = "";
  public bool IsAdminCategory { get; set; }

  public List<Link> Links { get; set; } = new ();
}

namespace h2s.Models;

public class Link
{
  public int Id { get; set; }
  public int CategoryId { get; set; }
  public string Label { get; set; } = "";
  public string Url { get; set; } = "";
  public int SortOrder { get; set; }

  public Category? Category { get; set; }
}

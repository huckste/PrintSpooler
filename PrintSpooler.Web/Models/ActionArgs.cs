namespace PrintSpooler.Web.Models;

public class ActionArgs
{
  public HashSet<Guid>? Ids { get; set; } = [];
  public RowActions Action { get; set; }
  public Guid PrinterId { get; set; }
}

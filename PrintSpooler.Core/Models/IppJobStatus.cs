namespace PrintSpooler.Core.Models;

public class IppJobStatus
{
  public int? Id { get; set; }
  public JobStatus State { get; set; }
  public string? Message { get; set; }
}

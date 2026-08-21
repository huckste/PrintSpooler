namespace PrintSpooler.Core.Models;

public class PrinterResponse
{
  public PrinterStatus Status { get; set; }
  public bool IsAccepting { get; set; }
  public string? Reason { get; set; }
}

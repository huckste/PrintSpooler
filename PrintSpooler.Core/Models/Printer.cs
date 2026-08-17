namespace PrintSpooler.Core.Models;

public class Printer
{
  public Guid Id { get; set; }
  public required string Name { get; set; }
  public required string IpAddress { get; set; }
  public PrinterStatus Status { get; set; } = PrinterStatus.Unknown;
  public Guid? FailoverPrinterId { get; set; }
  public DateTime? LastHeartbeat { get; set; }

  // newly added 
  public string? Host { get; set; }
  public List<string>? SupportedContentTypes { get; set; }
  public string? PrinterUuid { get; set; }
}

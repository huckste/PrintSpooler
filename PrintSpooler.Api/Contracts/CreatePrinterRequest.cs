namespace PrintSpooler.Api.Contracts;

public class CreatePrinterRequest
{
  public required string Name { get; set; }
  public required string IpAddress { get; set; }
  public string? Host { get; set; }
  public List<string>? SupportedContentTypes { get; set; }
  public string? PrinterUuid { get; set; }
}

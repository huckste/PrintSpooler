namespace PrintSpooler.Api.Contracts;

/// <summary>
/// Inbound payload for <c>POST /Printer</c>
/// carries only client-controlled fields.
/// </summary>

public class CreatePrinterRequest
{
  public required string Name { get; set; }
  public required string IpAddress { get; set; }
  /// <summary> Ipp host name - full uri is handled in <c>IPrinterDispatcher</c>. </summary>
  public string? Host { get; set; }
  public List<string>? SupportedContentTypes { get; set; }
  public string? PrinterUuid { get; set; }
}

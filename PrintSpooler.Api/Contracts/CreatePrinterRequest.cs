namespace PrintSpooler.Api.Contracts;

public class CreatePrinterRequest
{
    public required string Name { get; set; }
    public required string IpAddress { get; set; }
}

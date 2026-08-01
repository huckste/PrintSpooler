namespace PrintSpooler.Core.Models;

public class JobData
{
    public required byte[] Bytes { get; set; }
    public required Guid JobId { get; set; }
}

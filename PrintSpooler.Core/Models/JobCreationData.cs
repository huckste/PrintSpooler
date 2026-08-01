namespace PrintSpooler.Core.Models;

public class JobCreationData
{
    public required string SubmittedBy { get; set; }
    public required string FileName { get; set; }
    public required byte[] Bytes { get; set; }
    public required string ContentType { get; set; }
    public required Guid PrinterId { get; set; }
}

namespace PrintSpooler.Api.Contracts;

public class CreateJobRequest
{
    public required string SubmittedBy { get; set; }
    public required string FileName { get; set; }
    public required byte[] RawData { get; set; }
    public required string ContentType { get; set; }
    public required Guid PrinterId { get; set; }
}

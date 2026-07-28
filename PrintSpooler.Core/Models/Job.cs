namespace PrintSpooler.Core.Models;

public class Job
{
    public Guid Id { get; set; }
    public required string SubmittedBy { get; set; }
    public required string FileName { get; set; }
    public required byte[] RawData { get; set; }
    public required string ContentType { get; set; }
    public Guid PrinterId { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
}

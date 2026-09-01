namespace PrintSpooler.Core.Models;

public class Job
{
  public Guid Id { get; set; }
  public JobStatus Status { get; set; }
  public int? IppJobId { get; set; }
  public Printer? Printer { get; set; }
  public Guid PrinterId { get; set; }
  public JobData? Data { get; set; }
  public required string FileName { get; set; }
  public required string ContentType { get; set; }
  public required string SubmittedBy { get; set; }
  public int RetryCount { get; set; } = 0;
  public int MaxRetries { get; set; } = 3;
  public long FileSizeBytes { get; set; }
  public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
  public DateTime? CompletedAt { get; set; }
  public string? FailureReason { get; set; }
}

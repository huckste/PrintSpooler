namespace PrintSpooler.Core.Models;

public class Job
{
  public Guid Id { get; set; }
  public Printer? Printer { get; set; }
  public JobData? Data { get; set; }
  public required string SubmittedBy { get; set; }
  public required string FileName { get; set; }
  public required string ContentType { get; set; }
  public long FileSizeBytes { get; set; }
  public Guid PrinterId { get; set; }
  public OperationState Status { get; set; } = OperationState.Queued;
  public int RetryCount { get; set; } = 0;
  public int MaxRetries { get; set; } = 3;
  public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
  public DateTime? CompletedAt { get; set; }
  public string? FailureReason { get; set; }
}

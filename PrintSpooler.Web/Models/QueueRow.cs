using PrintSpooler.Core.Models;

namespace PrintSpooler.Web.Models;

public class QueueRow
{
  public Guid Id { get; init; } = Guid.NewGuid();
  public Guid PrinterId { get; init; }
  public string FileName { get; init; } = string.Empty;
  public string ContentType { get; init; } = string.Empty;

  public OperationState Status { get; set; }
  public long SizeBytes { get; set; }
  public int RetryCount { get; set; }
  public DateTime? SubmittedAt { get; set; }
  public string? ErrorText { get; set; }

  public byte[]? PendingData { get; set; }
  public Guid? JobId { get; set; }

  public bool IsSending { get; set; }

  public static QueueRow FromJob(Job job) => new()
  {
    PrinterId = job.PrinterId,
    FileName = job.FileName,
    ContentType = job.ContentType,
    Status = job.Status,
    SizeBytes = job.FileSizeBytes,
    RetryCount = job.RetryCount,
    SubmittedAt = job.SubmittedAt,
    ErrorText = job.FailureReason,
    JobId = job.Id
  };

  public void ApplyJob(Job job)
  {
    Status = job.Status;
    SizeBytes = job.FileSizeBytes;
    RetryCount = job.RetryCount;
    SubmittedAt = job.SubmittedAt;
    ErrorText = job.FailureReason;
    JobId = job.Id;
  }
}
namespace PrintSpooler.Core.Models;

public enum JobStatus
{
  Queued,
  Submitting,
  Processing,
  Stopped,
  Cancelling,
  Completed,
  Cancelled,
  Failed
}

namespace PrintSpooler.Core.Models;

public enum JobStatus
{
  Staged,
  Queued,
  Submitting,
  Processing,
  Cancelled,
  Completed,
  Failed,
  Unknown
}

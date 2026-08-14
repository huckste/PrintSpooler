namespace PrintSpooler.Core.Models;

public enum OperationState
{
  Staged,
  Queued,
  Submitting,
  Cancelled,
  Completed,
  Failed,
}

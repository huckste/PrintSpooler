namespace PrintSpooler.Core.Models;

public enum JobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
    Cancelled,
}

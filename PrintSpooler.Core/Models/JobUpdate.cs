namespace PrintSpooler.Core.Models;

public class JobUpdate(Guid id, JobStatus status)
{
  public Guid JobId = id;
  public JobStatus Status = status;
  public bool Retry;
  public bool Notify;
  public bool Write;
  public AuditLog? AuditLog;
  public string? FailureReason;

  public JobUpdate NotifyDashboard()
  {
    Notify = true;
    return this;
  }

  public JobUpdate RetryJob()
  {
    Retry = true;
    return this;
  }

  public JobUpdate WriteToChannel()
  {
    Write = true;
    return this;
  }

  public JobUpdate Log(JobAction action, ByWho by, string? reason = null)
  {
    AuditLog = AuditLog.For(JobId, action, by, reason);
    FailureReason = reason;
    return this;
  }
}

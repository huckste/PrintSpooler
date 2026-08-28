using ErrorOr;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public static class JobPolicies
{
  public static readonly JobStatus DefaultStatus = JobStatus.Queued;

  public static readonly JobStatus[] Terminal = [JobStatus.Completed, JobStatus.Cancelled];
  public static readonly JobStatus[] Pending = [JobStatus.Queued];
  public static readonly JobStatus[] InFlight = [JobStatus.Submitting, JobStatus.Processing];
  public static readonly JobStatus[] Retryable = [JobStatus.Failed];
  public static readonly JobStatus[] Active = [.. Enum.GetValues<JobStatus>().Except(Terminal)];

  public static bool IsPending(JobStatus status) => Pending.Contains(status);
  public static bool IsTerminal(JobStatus status) => Terminal.Contains(status);
  public static bool IsActive(JobStatus status) => Active.Contains(status);
  public static bool IsInFlight(JobStatus status) => InFlight.Contains(status);

  public static ErrorOr<Job> CanCancel(Job job) => Active.Contains(job.Status) ? job
    : Error.Conflict(
        "Job.CannotCancel",
        $"Job {job.Id} cannot be cancelled with current status: {job.Status}"
      );

  public static ErrorOr<Job> CanRetry(Job job) => Retryable.Contains(job.Status) ? job
    : Error.Conflict(
        "Job.CannotRetry",
        $"Job {job.Id} cannot be retried with current status: {job.Status}"
      );

  public static ErrorOr<Job> CanDispatch(Job job) => Pending.Contains(job.Status) ? job
    : Error.Conflict(
        "Job.CannotDispatch",
        $"Job {job.Id} cannot be dispatched with current status: {job.Status}"
      );
}

using ErrorOr;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public static class JobPolicies
{
  public static readonly JobStatus DefaultStatus = JobStatus.Queued;

  // EF translates array.Contains(column) to SQL IN
  public static readonly JobStatus[] Terminal = StatusesIn(PolicyGroup.Terminal);
  public static readonly JobStatus[] Pending = StatusesIn(PolicyGroup.Pending);
  public static readonly JobStatus[] InFlight = StatusesIn(PolicyGroup.InFlight);
  public static readonly JobStatus[] Retryable = StatusesIn(PolicyGroup.Retryable);
  public static readonly JobStatus[] Active = [.. Enum.GetValues<JobStatus>().Except(Terminal)];

  // A future group isn't cancellable until someone says so
  public static readonly JobStatus[] Cancellable =
    [.. Pending, .. Retryable, .. InFlight.Except([JobStatus.Cancelling])];

  public static bool IsPending(JobStatus status) => Pending.Contains(status);
  public static bool IsTerminal(JobStatus status) => Terminal.Contains(status);
  public static bool IsActive(JobStatus status) => Active.Contains(status);
  public static bool IsInFlight(JobStatus status) => InFlight.Contains(status);
  public static bool IsCancellable(JobStatus status) => Cancellable.Contains(status);

  // Non-exhaustive on purpose: a new JobStatus fails the build until it's added here
  public static PolicyGroup GetPolicyGroup(JobStatus status) => status switch
  {
    JobStatus.Completed => PolicyGroup.Terminal,
    JobStatus.Cancelled => PolicyGroup.Terminal,
    JobStatus.Queued => PolicyGroup.Pending,
    JobStatus.Submitting => PolicyGroup.InFlight,
    JobStatus.Processing => PolicyGroup.InFlight,
    JobStatus.Stopped => PolicyGroup.InFlight,
    JobStatus.Cancelling => PolicyGroup.InFlight,
    JobStatus.Failed => PolicyGroup.Retryable
  };

  // Built once on first use, so GetPolicyGroup stays single source of truth
  private static JobStatus[] StatusesIn(PolicyGroup group) =>
    [.. Enum.GetValues<JobStatus>().Where(s => GetPolicyGroup(s) == group)];

  public static ErrorOr<Job> CanCancel(Job job)
  {
    if (!Cancellable.Contains(job.Status))
      return Error.Conflict(
        "Job.CannotCancel",
        $"Job {job.Id} cannot be cancelled with current status: {job.Status}"
      );

    if (job.Status is JobStatus.Submitting && job.IppJobId is null)
      return Error.Conflict(
        "Job.Sending",
        $"Job {job.Id} is being sent to the printer and cannot be cancelled yet"
      );

    return job;
  }

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

using ErrorOr;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public static class JobPolicies
{
  public static ErrorOr<Job> CanCancel(Job job) =>
    job.Status is JobStatus.Queued or JobStatus.Failed ? job :
    Error.Conflict(
          "Job.CannotCancel",
          $"Job {job.Id} cannot be cancelled with current status: {job.Status}");

  public static ErrorOr<Job> CanRetry(Job job) =>
    job.Status is JobStatus.Failed ? job :
      Error.Conflict(
         "Job.CannotRetry",
         $"Job {job.Id} cannot be retried with current status: {job.Status}"
     );

  public static ErrorOr<Job> CanDispatch(Job job) =>
      job.Status is JobStatus.Queued ? job :
        Error.Conflict(
           "Job.CannotSend",
           $"Job {job.Id} cannot be sent with current status: {job.Status}"
       );

  public static bool IsPending(JobStatus status) => status is JobStatus.Queued;

  public static JobStatus DefaultStatus() => JobStatus.Queued;

  public static bool IsMia(JobStatus status) =>
    status is JobStatus.Submitting or JobStatus.Processing;

  public static bool IsTerminal(JobStatus status) =>
    status is JobStatus.Cancelled or JobStatus.Completed;

  public static bool IsActive(JobStatus status) =>
    Active.Contains(status);

  public static readonly JobStatus[] Mia = [JobStatus.Processing, JobStatus.Submitting];

  public static readonly JobStatus[] Pending = [JobStatus.Queued];

  public static readonly JobStatus[] Active =
    [
      JobStatus.Queued,
      JobStatus.Submitting,
      JobStatus.Processing,
      JobStatus.Failed,
      JobStatus.Unknown,
      JobStatus.Staged
    ];
}

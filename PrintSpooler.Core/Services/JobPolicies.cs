using ErrorOr;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public static class JobPolicies
{
  public static ErrorOr<Job> CanCancel(Job job) =>
    job.Status == JobStatus.Queued || job.Status == JobStatus.Failed ? job :
    Error.Conflict(
          "Job.CannotCancel",
          $"Job {job.Id} cannot be cancelled with current status: {job.Status}");

  public static ErrorOr<Job> CanRetry(Job job) =>
    job.Status == JobStatus.Failed ? job :
      Error.Conflict(
         "Job.CannotRetry",
         $"Job {job.Id} cannot be retried - current status is {job.Status}"
     );
}

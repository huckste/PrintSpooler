namespace PrintSpooler.Core.Services;

using ErrorOr;
using PrintSpooler.Core.Models;

public interface IJobService
{
  Task<ErrorOr<Job>> CreateJob(JobCreationData data);
  Task<ErrorOr<Job>> GetJob(Guid id);
  Task<List<Job>> GetAllActiveJobs();
  Task<ErrorOr<Job>> CancelJob(Guid id);
  Task<ErrorOr<Job>> RetryJob(Guid id);
  Task<ErrorOr<List<Job>>> GetPendingJobs();
  Task<ErrorOr<List<Job>>> GetMiaJobs();
  Task<ErrorOr<JobData>> GetJobData(Guid jobId, CancellationToken ct = default);
  Task<ErrorOr<Success>> UpdateJob(JobUpdate update, CancellationToken ct = default);
  Task<ErrorOr<Success>> RemoveJobData(Guid jobId, CancellationToken ct = default);
}

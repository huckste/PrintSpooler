namespace PrintSpooler.Core.Services;

using ErrorOr;
using PrintSpooler.Core.Models;

public interface IJobService
{
    Task<ErrorOr<Job>> CreateJob(JobCreationData data);
    Task<ErrorOr<Job>> GetJob(Guid id);
    Task<List<Job>> GetAllActiveJobs();
    Task<ErrorOr<Job>> CancelJob(Guid id);
}

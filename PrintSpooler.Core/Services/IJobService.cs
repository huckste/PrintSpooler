namespace PrintSpooler.Core.Services;

using ErrorOr;
using PrintSpooler.Core.Models;

public interface IJobService
{
    Task<ErrorOr<Job>> CreateJob(JobCreationData data);
}

namespace PrintSpooler.Infrastructure.Services;

using System.Threading.Channels;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

public class JobService(
    AppDbContext dbContext,
    Channel<Guid> jobChannel,
    IJobNotifier jobNotifier
    ) : IJobService
{
  public async Task<ErrorOr<Job>> CreateJob(JobCreationData data)
  {
    Printer? printerRes = await dbContext.Printers
      .FirstOrDefaultAsync(p => p.Id == data.PrinterId);

    if (printerRes is not { } printer)
      return Error.NotFound("Printer.NotFound", $"No printer found with ID {data.PrinterId}");

    var job = new Job
    {
      Id = Guid.NewGuid(),
      SubmittedBy = data.SubmittedBy,
      FileName = data.FileName,
      ContentType = data.ContentType,
      FileSizeBytes = data.Bytes.Length,
      PrinterId = data.PrinterId,
      Printer = printer,
      Status = JobPolicies.DefaultStatus()
    };

    dbContext.Jobs.Add(job);
    dbContext.JobData.Add(new JobData { Bytes = data.Bytes, JobId = job.Id });
    await dbContext.SaveChangesAsync();

    var result = await UpdateJob(
        new JobUpdate(job.Id, job.Status)
          .Log(JobAction.Created, ByWho.User)
          .WriteToChannel()
      );

    if (result.IsError)
      return result.Errors;

    return job;
  }

  public async Task<ErrorOr<Job>> GetJob(Guid id)
  {
    Job? result = await dbContext.Jobs.Include(j => j.Printer).FirstOrDefaultAsync(j => j.Id == id);

    if (result is not { } job)
      return Error.NotFound("Job.NotFound", $"No job found with ID {id}");

    return job;
  }

  public async Task<List<Job>> GetAllActiveJobs() =>
      await dbContext
          .Jobs.Include(j => j.Printer)
          .Where(j => JobPolicies.Active.Contains(j.Status))
          .ToListAsync();

  public async Task<ErrorOr<JobData>> GetJobData(Guid jobId, CancellationToken ct = default)
  {
    JobData? result = await dbContext.JobData.FirstOrDefaultAsync(d => d.JobId == jobId, ct);

    if (result is not { } jobData)
      return Error.NotFound("JobData.NotFound", $"No job data found for job ID {jobId}");

    return jobData;
  }

  public async Task<ErrorOr<List<Job>>> GetMiaJobs()
  {
    var miaJobs = await dbContext
            .Jobs.Include(j => j.Printer)
            .Where(j => JobPolicies.Mia.Contains(j.Status))
            .ToListAsync();

    return miaJobs.Count > 0
      ? miaJobs
      : Error.NotFound("Jobs.NotFound", "No MIA jobs found");
  }

  public async Task<ErrorOr<List<Job>>> GetPendingJobs()
  {
    var pendingJobs = await dbContext
            .Jobs.Include(j => j.Printer)
            .Where(j => JobPolicies.Pending.Contains(j.Status))
            .ToListAsync();

    return pendingJobs.Count > 0
      ? pendingJobs
      : Error.NotFound("Jobs.NotFound", "No pending jobs found");
  }

  public async Task<ErrorOr<Job>> CancelJob(Guid id)
  {
    var job = await GetJob(id);

    var update = await job.Then(JobPolicies.CanCancel)
      .ThenAsync(j =>
        UpdateJob(
          new JobUpdate(j.Id, JobStatus.Cancelled)
          .Log(JobAction.Cancelled, ByWho.User)
          .NotifyDashboard()
        )
      );

    return update.IsError ? update.Errors : job;
  }

  public async Task<ErrorOr<Job>> RetryJob(Guid id)
  {
    var job = await GetJob(id);

    var update = await job
      .Then(JobPolicies.CanRetry)
      .ThenAsync(j =>
        UpdateJob(
          new JobUpdate(j.Id, JobStatus.Queued)
          .RetryJob()
          .Log(JobAction.Retried, ByWho.User)
          .NotifyDashboard()
          .WriteToChannel()
        )
      );

    return update.IsError ? update.Errors : job;
  }

  public async Task<ErrorOr<Success>> RemoveJobData(Guid jobId, CancellationToken ct = default)
  {
    int rowsDeleted = await dbContext.JobData
      .Where(d => d.JobId == jobId)
      .ExecuteDeleteAsync(ct);

    Error? error = rowsDeleted switch
    {
      < 1 => Error.Failure("Job.RemoveJobData", $"Failed to delete job data for id: {jobId}"),
      > 1 => Error.Unexpected("Job.RemoveJobData", $"Deleted more than one row: {rowsDeleted}"),
      _ => null
    };

    return error == null ? Result.Success : (Error)error;
  }

  public async Task<ErrorOr<Success>> UpdateJob(JobUpdate update, CancellationToken ct = default)
  {
    if (update.AuditLog != null)
      dbContext.AuditLogs.Add(update.AuditLog);

    ErrorOr<Job> job = await GetJob(update.JobId)
      .Then(j =>
      {
        j.Status = update.Status;

        if (j.Status is JobStatus.Failed)
          j.FailureReason = update.FailureReason;

        if (update.Retry)
          j.RetryCount++;

        return j;
      });

    if (job.IsError)
      return job.Errors;

    if (JobPolicies.IsTerminal(job.Value.Status))
    {
      var rowsDeleted = await RemoveJobData(job.Value.Id);

      if (rowsDeleted.IsError)
        return rowsDeleted.Errors;
    }

    await dbContext.SaveChangesAsync(ct);

    if (update.Notify)
      await jobNotifier.JobUpdateAsync(job.Value, ct);

    if (update.Write)
      await jobChannel.Writer.WriteAsync(job.Value.Id);


    return Result.Success;

  }

}

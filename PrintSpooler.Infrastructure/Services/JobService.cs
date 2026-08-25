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
    Printer? printer = await dbContext.Printers.FirstOrDefaultAsync(p =>
        p.Id == data.PrinterId
    );

    if (printer is null)
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

    await UpdateJob(
        new JobUpdate(job.Id, job.Status)
          .Log(JobAction.Created, ByWho.User)
          .WriteToChannel()
      );

    return job;
  }

  public async Task<ErrorOr<Job>> GetJob(Guid id)
  {
    var job = await dbContext.Jobs.Include(j => j.Printer).FirstOrDefaultAsync(j => j.Id == id);

    if (job is null)
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
    JobData? jobData = await dbContext.JobData.FirstOrDefaultAsync(d => d.JobId == jobId, ct);

    if (jobData == null)
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

  public async Task<ErrorOr<Job>> CancelJob(Guid id) =>
    await GetJob(id)
      .Then(JobPolicies.CanCancel)
      .ThenDoAsync(j =>
          UpdateJob(
            new JobUpdate(j.Id, JobStatus.Cancelled)
              .Log(JobAction.Cancelled, ByWho.User)
              .NotifyDashboard()
              )
          );

  public async Task<ErrorOr<Job>> RetryJob(Guid id) =>
    await GetJob(id)
      .Then(JobPolicies.CanRetry)
      .ThenDoAsync(j =>
           UpdateJob(
             new JobUpdate(j.Id, JobStatus.Queued)
              .RetryJob()
              .Log(JobAction.Retried, ByWho.User)
              .NotifyDashboard()
              .WriteToChannel()
              )
          );

  public async Task RemoveJobData(Guid jobId, CancellationToken ct = default) =>
    await dbContext.JobData
      .Where(d => d.JobId == jobId)
      .ExecuteDeleteAsync(ct);

  public async Task UpdateJob(JobUpdate update, CancellationToken ct = default)
  {
    if (update.AuditLog != null)
      dbContext.AuditLogs.Add(update.AuditLog);

    var job = await dbContext.Jobs.FirstAsync(j => j.Id == update.JobId, ct);

    job.Status = update.Status;

    if (job.Status is JobStatus.Failed)
      job.FailureReason = update.FailureReason;

    if (JobPolicies.IsTerminal(job.Status))
      await RemoveJobData(job.Id);

    if (update.Retry)
      job.RetryCount++;

    await dbContext.SaveChangesAsync(ct);

    if (update.Notify)
      await jobNotifier.JobUpdateAsync(job, ct);

    if (update.Write)
      await jobChannel.Writer.WriteAsync(job.Id);
  }

}

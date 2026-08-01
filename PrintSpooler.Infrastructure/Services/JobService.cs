namespace PrintSpooler.Infrastructure.Services;

using System.Threading.Channels;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

public class JobService(AppDbContext dbContext, Channel<Guid> jobChannel, IJobNotifier jobNotifier)
    : IJobService
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
            PrinterId = data.PrinterId,
            Printer = printer,
        };

        dbContext.Jobs.Add(job);
        dbContext.JobData.Add(new JobData { Bytes = data.Bytes, JobId = job.Id });

        dbContext.AuditLogs.Add(
            new AuditLog
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Action = JobAction.Created,
                PerformedBy = ByWho.User,
            }
        );

        await dbContext.SaveChangesAsync();
        await jobChannel.Writer.WriteAsync(job.Id);

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
            .Where(j => j.Status != JobStatus.Cancelled && j.Status != JobStatus.Completed)
            .ToListAsync();

    public async Task<ErrorOr<Job>> CancelJob(Guid id)
    {
        var job = await dbContext.Jobs.Include(j => j.Printer).FirstOrDefaultAsync(j => j.Id == id);

        if (job is null)
            return Error.NotFound("Job.NotFound", $"No job found with ID {id}");

        bool canCancel = JobCancellationPolicy.CanCancel(job.Status);

        if (!canCancel)
            return Error.Conflict(
                "Job.CannotCancel",
                $"Job {id} cannot be cancelled - current status is {job.Status}"
            );

        job.Status = JobStatus.Cancelled;

        dbContext.AuditLogs.Add(
            new AuditLog
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Action = JobAction.Cancelled,
                PerformedBy = ByWho.User,
            }
        );

        await dbContext.SaveChangesAsync();
        await jobNotifier.JobUpdateAsync(job, CancellationToken.None);

        return job;
    }
}

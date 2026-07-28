namespace PrintSpooler.Infrastructure.Services;

using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

public class JobService(AppDbContext dbContext) : IJobService
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
            RawData = data.RawData,
            ContentType = data.ContentType,
            PrinterId = data.PrinterId,
            Printer = printer,
        };

        dbContext.Jobs.Add(job);
        await dbContext.SaveChangesAsync();

        return job;
    }

    public async Task<ErrorOr<Job>> GetJob(Guid id)
    {
        var job = await dbContext.Jobs.Include(j => j.Printer).FirstOrDefaultAsync(j => j.Id == id);

        if (job is null)
            return Error.NotFound("Job.NotFound", $"No job found with ID {id}");

        return job;
    }
}

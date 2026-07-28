using Microsoft.AspNetCore.Mvc;
using PrintSpooler.Api.Contracts;
using PrintSpooler.Core.Models;
using PrintSpooler.Infrastructure.Data;

namespace PrintSpooler.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class PrintJobController(AppDbContext dbContext) : ControllerBase
{
    [HttpPost(Name = "PostPrintJob")]
    public async Task<IActionResult> Post(CreateJobRequest jobRequest)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            SubmittedBy = jobRequest.SubmittedBy,
            FileName = jobRequest.FileName,
            RawData = jobRequest.RawData,
            ContentType = jobRequest.ContentType,
            PrinterId = jobRequest.PrinterId,
        };

        dbContext.Jobs.Add(job);
        await dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(Post), new { id = job.Id }, job);
    }
}

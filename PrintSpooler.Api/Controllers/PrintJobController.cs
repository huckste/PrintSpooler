using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using PrintSpooler.Api.Contracts;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class PrintJobController(IJobService jobService) : ControllerBase
{
    [HttpPost(Name = "PostPrintJob")]
    public async Task<IActionResult> Post(CreateJobRequest jobRequest)
    {
        return await jobService
            .CreateJob(
                new JobCreationData
                {
                    PrinterId = jobRequest.PrinterId,
                    SubmittedBy = jobRequest.SubmittedBy,
                    RawData = jobRequest.RawData,
                    ContentType = jobRequest.ContentType,
                    FileName = jobRequest.FileName,
                }
            )
            .Match(
                job => CreatedAtAction(nameof(Post), new { id = job.Id }, job),
                errors => Problem(detail: errors.First().Description, statusCode: 400)
            );
    }
}

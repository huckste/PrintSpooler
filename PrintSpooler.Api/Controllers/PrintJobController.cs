using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using PrintSpooler.Api.Contracts;
using PrintSpooler.Api.Extensions;
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
              Bytes = jobRequest.RawData,
              ContentType = jobRequest.ContentType,
              FileName = jobRequest.FileName,
            }
        )
        .Match(
            job => CreatedAtAction(nameof(Post), new { id = job.Id }, job),
            errors => Problem(detail: errors.First().Description, statusCode: errors.ToStatusCode())
        );
  }

  [HttpGet("{id}", Name = "GetPrintJob")]
  public async Task<IActionResult> Get(Guid id)
  {
    return await jobService
        .GetJob(id)
        .Match(Ok, errors => Problem(detail: errors.First().Description, statusCode: errors.ToStatusCode()));
  }

  [HttpGet(Name = "GetAllActivePrintJob")]
  public async Task<IActionResult> Get() => Ok(await jobService.GetAllActiveJobs());

  [HttpPost("{id}/cancel", Name = "CancelPrintJob")]
  public async Task<IActionResult> Cancel(Guid id)
  {
    return await jobService
        .CancelJob(id)
        .Match(Ok, errors => Problem(detail: errors.First().Description, statusCode: errors.ToStatusCode()));
  }

  [HttpPost("{id}/retry", Name = "RetryPrintJob")]
  public async Task<IActionResult> Retry(Guid id)
  {
    return await jobService
        .RetryJob(id)
        .Match(Ok, errors => Problem(detail: errors.First().Description, statusCode: errors.ToStatusCode()));
  }
}

using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Api.IntegrationTests;

public static class PrintJobTestHelper
{
  public static Job CreatePrintJobByStatus(JobStatus status)
  {
    Guid jobId = Guid.NewGuid();

    return new Job
    {
      SubmittedBy = "IntegrationTest",
      ContentType = "Test_Data",
      FileName = $"TestJob_{status}",
      Status = status,
      Id = jobId,
      Printer = new Printer { Name = "Test_Printer", IpAddress = "000.000.00.00" },

      // Once a job is Terminal then its ByteData is removed from the DB in Prod
      Data = JobPolicies.IsTerminal(status)
        ? null
        : new JobData { JobId = jobId, Bytes = "test_data"u8.ToArray() },
    };
  }
}

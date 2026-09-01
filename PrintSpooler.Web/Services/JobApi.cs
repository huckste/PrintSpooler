using ErrorOr;
using PrintSpooler.Core.Models;
using PrintSpooler.Web.Models;

namespace PrintSpooler.Web.Services;

public class JobApi(ApiClient api)
{
  public Task<ErrorOr<List<Job>>> GetJobs() =>
      api.Get<Job>("/PrintJob");

  public Task<ErrorOr<Job>> SubmitJob(QueueRow row) =>
    api.Post<Job>("/PrintJob", new
    {
      row.FileName,
      row.ContentType,
      RawData = row.PendingData,
      SubmittedBy = "dashboard-user",
      row.PrinterId
    });

  public Task<ErrorOr<Job>> RetryJob(Guid? id) =>
    api.Post<Job>($"/PrintJob/{id}/retry", new { });

  public Task<ErrorOr<Job>> CancelJob(Guid? id) =>
    api.Post<Job>($"/PrintJob/{id}/cancel", new { });

}


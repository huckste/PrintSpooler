using PrintSpooler.Core.Models;

namespace PrintSpooler.Web.Services;

public static class RowPolicies
{
  public static bool CanCancel(JobStatus status) =>
      status == JobStatus.Staged || status == JobStatus.Failed;

  public static bool CanRetry(JobStatus status) =>
    status == JobStatus.Failed;

  public static bool CanSend(JobStatus status) =>
      status == JobStatus.Staged;

}

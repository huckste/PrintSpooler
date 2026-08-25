using PrintSpooler.Core.Models;
using PrintSpooler.Web.Models;

namespace PrintSpooler.Web.Services;

public static class JobWebService
{

  public static int CountByStatus(JobStatus status, Dictionary<Guid, List<QueueRow>> rowsDict) =>
    ToList(rowsDict).Count(row => row.Status == status);

  public static int CountByStatus(JobStatus status, List<QueueRow> rows) =>
      rows.Count(row => row.Status == status);

  public static List<QueueRow> ToList(Dictionary<Guid, List<QueueRow>> rowsDict, RowActions action, Guid? printerId = null, HashSet<Guid>? rowIds = null)
  {
    return [.. rowsDict.Values
      .SelectMany(rowList => rowList)
      .Where(r => TargetedRow(r, rowIds)
          && CanDoAction(action, r.Status)
          && (printerId == null || printerId == r.PrinterId))];
  }

  public static List<QueueRow> ToList(Dictionary<Guid, List<QueueRow>> rowsDict) =>
    [.. rowsDict.Values.SelectMany(rowList => rowList)];

  public static bool TargetedRow(QueueRow row, HashSet<Guid>? ids) => ids is null || ids.Contains(row.Id);

  public static bool CanDoAction(RowActions action, JobStatus status) =>
    action switch
    {
      RowActions.Delete => RowPolicies.CanCancel(status),
      RowActions.Retry => RowPolicies.CanRetry(status),
      RowActions.Send => RowPolicies.CanSend(status),
      _ => false
    };

}

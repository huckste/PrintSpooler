using PrintSpooler.Core.Models;
using PrintSpooler.Web.Models;

namespace PrintSpooler.Web.Services;

public static class JobWebService
{
  // null counts staged rows — same convention as QueueRow.Status.
  public static int CountByStatus(JobStatus? status, Dictionary<Guid, List<QueueRow>> rowsDict) =>
    ToList(rowsDict).Count(row => row.Status == status);

  public static int CountByStatus(JobStatus? status, List<QueueRow> rows) =>
      rows.Count(row => row.Status == status);

  public static List<QueueRow> ToList(Dictionary<Guid, List<QueueRow>> rowsDict, RowActions action, Guid? printerId = null, HashSet<Guid>? rowIds = null)
  {
    return [.. rowsDict.Values
      .SelectMany(rowList => rowList)
      .Where(r => TargetedRow(r, rowIds)
          && CanDoAction(action, r)
          && (printerId == null || printerId == r.PrinterId))];
  }

  public static List<QueueRow> ToList(Dictionary<Guid, List<QueueRow>> rowsDict) =>
    [.. rowsDict.Values.SelectMany(rowList => rowList)];

  public static bool TargetedRow(QueueRow row, HashSet<Guid>? ids) => ids is null || ids.Contains(row.Id);

  // A row with a request already outstanding offers no actions
  public static bool CanDoAction(RowActions action, QueueRow row) =>
    !row.IsBusy && action switch
    {
      RowActions.Cancel => RowPolicies.CanCancel(row),
      RowActions.Retry => RowPolicies.CanRetry(row),
      RowActions.Send => RowPolicies.CanSend(row),
      _ => false
    };

}

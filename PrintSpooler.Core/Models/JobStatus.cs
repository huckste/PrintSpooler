namespace PrintSpooler.Core.Models;

public enum JobStatus
{
  ///<summary>A job is queued to be sent to a printer (not onboard printer queue) </summary>
  Queued,
  ///<summary>A job is being submitted to a printer </summary>
  Submitting,
  ///<summary>A printer is activily working on a print job </summary>
  Processing,
  Cancelling,
  Completed,
  Cancelled,
  Failed
}

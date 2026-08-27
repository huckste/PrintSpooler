namespace PrintSpooler.Api.Contracts;

/// <summary>
/// Inbound payload for <c>POST /PrintJob</c>
/// carries only client-controlled fields
/// </summary>

public class CreateJobRequest
{
  public required string SubmittedBy { get; set; }
  public required string FileName { get; set; }
  /// <summary> Must already be pritner-native format - no transcoding. </summary>
  public required byte[] RawData { get; set; }
  public required string ContentType { get; set; }
  public required Guid PrinterId { get; set; }
}

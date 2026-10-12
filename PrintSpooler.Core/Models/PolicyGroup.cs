namespace PrintSpooler.Core.Models;

/// <summary> Which lifecycle bucket a <see cref="JobStatus"/> belongs to </summary>
public enum PolicyGroup
{
  Terminal,
  Pending,
  InFlight,
  Retryable
}

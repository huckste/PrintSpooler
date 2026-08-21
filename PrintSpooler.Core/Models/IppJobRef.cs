
namespace PrintSpooler.Core.Models;

public sealed record IppJobRef(Guid PrinterId, Guid JobId, int IppId);

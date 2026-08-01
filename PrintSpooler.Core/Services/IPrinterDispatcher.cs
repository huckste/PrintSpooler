using ErrorOr;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public interface IPrinterDispatcher
{
    Task<ErrorOr<Success>> SendAsync(Job job, byte[]? jobData, CancellationToken ct);
}

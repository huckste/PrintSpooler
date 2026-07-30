using PrintSpooler.Core.Models;

namespace PrintSpooler.Core.Services;

public interface IJobNotifier
{
    Task JobUpdateAsync(Job job, CancellationToken ct);
}

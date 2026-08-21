using Microsoft.AspNetCore.SignalR;
using PrintSpooler.Api.Hubs;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;

namespace PrintSpooler.Api.Services;

public class JobNotifier(IHubContext<UpdatesHub> hubContext) : IJobNotifier
{
    public async Task JobUpdateAsync(Job job, CancellationToken ct)
    {
        await hubContext.Clients.All.SendAsync("JobUpdated", job, ct);
    }
}

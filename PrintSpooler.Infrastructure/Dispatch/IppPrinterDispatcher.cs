using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using SharpIpp;
using SharpIpp.Models.Requests;

namespace PrintSpooler.Infrastructure.Dispatch;

public class IppPrinterDispatcher : IPrinterDispatcher
{
    private readonly SharpIppClient _client = new();

    public async Task<bool> SendAsync(Job job, Printer printer, CancellationToken ct)
    {
        using var stream = new MemoryStream(job.RawData);

        var request = new PrintJobRequest
        {
            OperationAttributes = new()
            {
                PrinterUri = new Uri(printer.IpAddress),
                JobName = job.FileName,
                DocumentFormat = job.ContentType,
            },

            Document = stream,
        };

        var response = await _client.PrintJobAsync(request);
        return response.StatusCode == SharpIpp.Protocol.Models.IppStatusCode.SuccessfulOk;
    }
}

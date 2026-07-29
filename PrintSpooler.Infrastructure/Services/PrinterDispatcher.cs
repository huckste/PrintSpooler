using ErrorOr;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using SharpIpp;
using SharpIpp.Models.Requests;

namespace PrintSpooler.Infrastructure.Services;

public class PrinterDispatcher : IPrinterDispatcher
{
    private readonly SharpIppClient _client = new();

    public async Task<ErrorOr<Success>> SendAsync(Job job, CancellationToken ct)
    {
        if (job.Printer is null)
            return Error.Unexpected("Job.Unexpected", $"Printer can not have a value of null");

        using var stream = new MemoryStream(job.RawData);

        var request = new PrintJobRequest
        {
            OperationAttributes = new()
            {
                PrinterUri = new Uri(job.Printer.IpAddress),
                JobName = job.FileName,
                DocumentFormat = job.ContentType,
            },

            Document = stream,
        };

        try
        {
            var response = await _client.PrintJobAsync(request);

            return response.StatusCode == SharpIpp.Protocol.Models.IppStatusCode.SuccessfulOk
                ? Result.Success
                : Error.Failure("Printer.Failed", $"IPP status code: {response.StatusCode}");
        }
        catch (SharpIpp.Exceptions.IppResponseException ex)
        {
            return Error.Failure(
                "Job.Failure",
                $"IPP error: {ex.Message} | Response:{ex.ResponseMessage?.StatusCode}"
            );
        }
        catch (Exception ex)
        {
            return Error.Failure("Job.Failure", $"Print job failed: {ex.Message}");
        }
    }
}

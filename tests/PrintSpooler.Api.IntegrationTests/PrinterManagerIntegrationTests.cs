using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PrintSpooler.Core.Models;
using PrintSpooler.Infrastructure.Data;
using PrintSpooler.Infrastructure.Services;

namespace PrintSpooler.Api.IntegrationTests;

public class PrinterManagerIntegrationTests(SeededPrinterWebApplicationFactory webFactory) : IClassFixture<SeededPrinterWebApplicationFactory>
{
  private readonly HttpClient _client = webFactory.CreateClient();
  private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
  private readonly RecordingPrinterMonitorFactory _recorder = (RecordingPrinterMonitorFactory)webFactory.Services.GetRequiredService<IPrinterMonitorFactory>();

  private async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
  {
    using var cts = new CancellationTokenSource(timeout);

    while (!cts.IsCancellationRequested)
    {
      if (condition.Invoke())
        return true;

      await Task.Delay(100, _ct);
    }

    return false;
  }

  private async Task<Guid> CreatePrinter()
  {
    var printerRequest = PrinterTestHelper.CreateUniquePrinterRequest();

    var response = await _client.PostAsJsonAsync("/Printer", printerRequest, _ct);
    response.EnsureSuccessStatusCode();

    var printer = await response.Content.ReadFromJsonAsync<Printer>(_ct);
    Assert.NotNull(printer);

    return printer.Id;
  }

  private async Task AddPrintJobToDb(Guid printerId)
  {
    var job = PrintJobTestHelper.CreatePrintJobForPrinter(printerId);

    using var scope = webFactory.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    dbContext.Add(job);
    await dbContext.SaveChangesAsync(_ct);
  }

  private async Task<PrinterMonitor> CreatePrinterWithMonitor()
  {
    var printerId = await CreatePrinter();

    Assert.True(
            await WaitUntil(() =>
              _recorder.Monitors.ContainsKey(printerId),
              TimeSpan.FromSeconds(5)),
            "Precondition: monitor was never created"
            );

    var monitor = _recorder.Monitors[printerId];

    return monitor;
  }

  [Fact]
  public async Task CreatePrinter_Succeeds_StartsPrinterMonitor()
  {
    var printerId = await CreatePrinter();

    Assert.True(
        await WaitUntil(() =>
          _recorder.Monitors.ContainsKey(printerId),
          TimeSpan.FromSeconds(5))
        );
  }

  [Fact]
  public async Task Startup_PrinterInDb_StartsPrinterMonitor() =>
    Assert.True(
        await WaitUntil(() =>
          _recorder.Monitors.ContainsKey(webFactory.SeededPrinterId),
          TimeSpan.FromSeconds(5))
        );

  [Fact]
  public async Task DeletePrinter_Succeeds_DisposesPrinterMonitor()
  {
    var monitor = await CreatePrinterWithMonitor();
    var response = await _client.DeleteAsync($"/Printer/{monitor.Printer.Id}", _ct);

    response.EnsureSuccessStatusCode();

    Assert.True(await WaitUntil(() => monitor.IsDisposed, TimeSpan.FromSeconds(5)));
  }

  [Fact]
  public async Task DeletePrinterWithActiveJob_Fails_DoesNotDisposePrinterMonitor()
  {
    var monitor = await CreatePrinterWithMonitor();
    await AddPrintJobToDb(monitor.Printer.Id);

    var response = await _client.DeleteAsync($"/Printer/{monitor.Printer.Id}", _ct);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.NotNull(problem);
    Assert.Equal(409, problem.Status);

    // PrinterManager events are handled in order
    // This monitor being created means any Remove event from the blocked delete would have been handled
    await CreatePrinterWithMonitor();
    Assert.False(monitor.IsDisposed);
  }
}

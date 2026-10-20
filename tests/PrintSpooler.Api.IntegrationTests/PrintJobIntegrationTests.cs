using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;

namespace PrintSpooler.Api.IntegrationTests;

// xUnit Shared Context between Tests
// New class instance for each test
public class PrintJobIntegrationTests(CustomWebApplicationFactory webFactory)
  : IClassFixture<CustomWebApplicationFactory>
{
  private readonly HttpClient _client = webFactory.CreateClient();
  private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

  public static TheoryData<JobStatus> TerminalStatuses => [.. JobPolicies.Terminal];

  public async ValueTask SaveJobToDb(Job job)
  {
    using var scope = webFactory.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    dbContext.Jobs.Add(job);
    await dbContext.SaveChangesAsync(_ct);
  }

  [Fact]
  public async Task GetPrintJob_IdDoesNotExist_Returns404()
  {
    var id = Guid.NewGuid();
    var response = await _client.GetAsync($"/PrintJob/{id}", _ct);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.NotNull(problem);
    Assert.Equal(404, problem.Status);

    // Non generic message should contain failed PrintJob ID
    Assert.Contains(id.ToString(), problem.Detail);
  }

  [Theory]
  [MemberData(nameof(TerminalStatuses))]
  public async Task CancelPrintJob_JobHasTerminalState_Returns409(JobStatus status)
  {
    var job = PrintJobTestHelper.CreatePrintJobByStatus(status);
    await SaveJobToDb(job);

    var response = await _client.PostAsJsonAsync($"/PrintJob/{job.Id}/cancel", job, _ct);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.NotNull(problem);
    Assert.Equal(409, problem.Status);

    // Non generic message should contain failed PrintJob ID
    Assert.Contains(job.Id.ToString(), problem.Detail);
  }

  [Theory]
  [MemberData(nameof(TerminalStatuses))]
  public async Task RetryPrintJob_JobHasTerminalState_Returns409(JobStatus status)
  {
    var job = PrintJobTestHelper.CreatePrintJobByStatus(status);
    await SaveJobToDb(job);

    var response = await _client.PostAsJsonAsync($"/PrintJob/{job.Id}/retry", job, _ct);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_ct);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.NotNull(problem);
    Assert.Equal(409, problem.Status);

    // Non generic message should contain failed PrintJob ID
    Assert.Contains(job.Id.ToString(), problem.Detail);
  }
}

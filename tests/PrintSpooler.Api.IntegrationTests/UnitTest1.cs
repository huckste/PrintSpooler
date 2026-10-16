namespace PrintSpooler.Api.IntegrationTests;

// xUnit Shared Context between Tests
public class UnitTest1(CustomWebApplicationFactory webFactory)
  : IClassFixture<CustomWebApplicationFactory>
{
  public HttpClient client = webFactory.CreateClient();

  [Fact]
  public async Task Test1()
  {
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/health", ct);
    Assert.True(response.IsSuccessStatusCode);
  }
}

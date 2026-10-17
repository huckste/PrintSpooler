using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Infrastructure.Data;
using Testcontainers.MsSql;

namespace PrintSpooler.Api.IntegrationTests;

// WebApplicationFactory: Boots real API in memory and Program tells it which app to start
// IAsyncLifetime: lets xUnit call async setup and teardown around the tests
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
  // Describes a SQL server docker container
  private readonly MsSqlContainer _msSqlContainer = new MsSqlBuilder(
    "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04"
  ).Build();

  // Overrides the connection string so the API uses the container DB instead of real DB
  protected override void ConfigureWebHost(IWebHostBuilder builder) =>
    builder.UseSetting("ConnectionStrings:PrintSpoolerDb", _msSqlContainer.GetConnectionString());

  public async ValueTask InitializeAsync()
  {
    // Builds and starts the docker container
    await _msSqlContainer.StartAsync();

    // Touching `Services` starts the API
    // Certain hosted services will throw IF schema does not exist first
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlServer(_msSqlContainer.GetConnectionString())
      .Options;

    await using var dbContext = new AppDbContext(options);

    // Migrate with standalone context before anything touches `Services`
    await dbContext.Database.MigrateAsync();
  }

  public override async ValueTask DisposeAsync()
  {
    // Order: Shut down API THEN remove container
    // Background workers still query the DB and throw if the container is already gone
    await base.DisposeAsync();
    await _msSqlContainer.DisposeAsync();
  }
}

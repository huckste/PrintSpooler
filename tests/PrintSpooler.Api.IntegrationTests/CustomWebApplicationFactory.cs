using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using PrintSpooler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace PrintSpooler.Api.IntegrationTests;

// WebApplicationFactory: Boots real API in memory and Program tells it which app to start
// IAsyncLifetime: lets xUnit call async setup and teardown around the tests
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
  // describes a SQL server docker container. This only builds and does not start it
  private readonly MsSqlContainer _msSqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
  .Build();

  // runs when the API boots. It overrides the connection string so the API uses the container instead of real db
  protected override void ConfigureWebHost(IWebHostBuilder builder) =>
    builder.UseSetting("ConnectionStrings:PrintSpoolerDb", _msSqlContainer.GetConnectionString());

  public async ValueTask InitializeAsync()
  {
    // starts the container
    await _msSqlContainer.StartAsync();

    using var scope = Services.CreateScope();
    using var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // creates the tables in the empty db
    await dbContext.Database.MigrateAsync();
  }

  // xUnit runs this after the tests. Removes the container, then lets the factory shut the API down
  public override async ValueTask DisposeAsync()
  {
    await _msSqlContainer.DisposeAsync();
    await base.DisposeAsync();
  }

}


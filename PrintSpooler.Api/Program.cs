using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Api.Hubs;
using PrintSpooler.Api.Services;
using PrintSpooler.Core.Models;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;
using PrintSpooler.Infrastructure.Services;
using SharpIpp;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration["ConnectionStrings:PrintSpoolerDb"],
        sqlOptions => sqlOptions.CommandTimeout(120)
    )
);

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddSingleton<IPrinterMonitorFactory, PrinterMonitorFactory>();
builder.Services.AddSingleton<IPrinterDispatcher, PrinterDispatcher>();
builder.Services.AddSingleton<SharpIppClient>();
builder.Services.AddSingleton<IPrinterDiscoveryService, PrinterDiscoveryService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IPrinterService, PrinterService>();
builder.Services.AddSingleton<IJobNotifier, JobNotifier>();
builder.Services.AddSingleton<IPrinterNotifier, PrinterNotifier>();
builder.Services.AddScoped<ILogsService, LogsService>();
builder.Services.AddHostedService<PrinterManager>();
builder.Services.AddSingleton(_ => Channel.CreateUnbounded<JobRef>());
builder.Services.AddSingleton(_ => Channel.CreateUnbounded<PrinterEvent>());
builder.Services.AddSignalR();

var app = builder.Build();
app.MapHub<UpdatesHub>("/hubs/updates");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

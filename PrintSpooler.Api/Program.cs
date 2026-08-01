using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using PrintSpooler.Api.Hubs;
using PrintSpooler.Api.Services;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;
using PrintSpooler.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration["ConnectionStrings:PrintSpoolerDb"])
);

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddSingleton<IPrinterDispatcher, PrinterDispatcher>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IPrinterService, PrinterService>();
builder.Services.AddSingleton(_ => Channel.CreateUnbounded<Guid>());
builder.Services.AddHostedService<PrintJobWorker>();
builder.Services.AddSingleton<IJobNotifier, JobNotifier>();
builder.Services.AddSignalR();

var app = builder.Build();
app.MapHub<JobHub>("/hubs/jobs");

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

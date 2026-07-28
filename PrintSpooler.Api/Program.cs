using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Services;
using PrintSpooler.Infrastructure.Data;
using PrintSpooler.Infrastructure.Dispatch;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration["ConnectionStrings:PrintSpoolerDb"])
);

builder.Services.AddSingleton<IPrinterDispatcher, IppPrinterDispatcher>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

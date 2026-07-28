namespace PrintSpooler.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<Job> Jobs { get; set; }
    public DbSet<Printer> Printers { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
}

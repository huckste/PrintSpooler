namespace PrintSpooler.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs { get; set; }
    public DbSet<Printer> Printers { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
}

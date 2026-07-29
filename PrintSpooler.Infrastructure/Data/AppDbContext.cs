namespace PrintSpooler.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using PrintSpooler.Core.Models;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs { get; set; }
    public DbSet<Printer> Printers { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>().Property(j => j.Status).HasConversion<string>();

        modelBuilder.Entity<Printer>().Property(p => p.Status).HasConversion<string>();

        modelBuilder.Entity<AuditLog>().Property(a => a.Action).HasConversion<string>();

        modelBuilder.Entity<AuditLog>().Property(a => a.PerformedBy).HasConversion<string>();
    }
}

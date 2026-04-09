using Microsoft.EntityFrameworkCore;
using ProcurePortal.API.Models;

namespace ProcurePortal.API.Data;

public class ProcurementsDbContext : DbContext
{
    public ProcurementsDbContext(DbContextOptions<ProcurementsDbContext> options)
        : base(options)
    {
    }

    public DbSet<TenderNotice> TenderNotices { get; set; }
    public DbSet<TenderHeader> TenderHeaders { get; set; }
    public DbSet<TenderDocument> TenderDocuments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Map PostgreSQL array columns
        modelBuilder.Entity<TenderNotice>(entity =>
        {
            entity.Property(e => e.Unspsc).HasColumnType("integer[]");
            entity.Property(e => e.Gsin).HasColumnType("text[]");
        });
    }
}

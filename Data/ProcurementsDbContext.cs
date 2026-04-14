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
    public DbSet<CompanyProfile> CompanyProfiles { get; set; }
    public DbSet<CompanyPreferences> CompanyPreferences { get; set; }
    public DbSet<CompanyMatch> CompanyMatches { get; set; }
    public DbSet<CompanyCommodityType> CompanyCommodityTypes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Map PostgreSQL array columns
        modelBuilder.Entity<TenderNotice>(entity =>
        {
            entity.Property(e => e.Unspsc).HasColumnType("integer[]");
            entity.Property(e => e.Gsin).HasColumnType("text[]");
        });

        modelBuilder.Entity<CompanyProfile>(entity =>
        {
            entity.Property(e => e.Keywords).HasColumnType("varchar[]");
            entity.Property(e => e.UnspscCodes).HasColumnType("integer[]");
            entity.Property(e => e.GsinCodes).HasColumnType("varchar[]");
            entity.Property(e => e.Certifications).HasColumnType("varchar[]");

            entity.HasOne(e => e.Preferences)
                .WithOne(p => p.Company)
                .HasForeignKey<CompanyPreferences>(p => p.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompanyPreferences>(entity =>
        {
            entity.Property(e => e.PreferredOrgs).HasColumnType("varchar[]");
            entity.Property(e => e.PreferredNtTypes).HasColumnType("varchar[]");
            entity.Property(e => e.PreferredProvinces).HasColumnType("varchar[]");
            entity.Property(e => e.ExcludeKeywords).HasColumnType("varchar[]");
        });

        modelBuilder.Entity<CompanyMatch>(entity =>
        {
            entity.HasIndex(e => new { e.CompanyId, e.TenderId }).IsUnique();
        });
    }
}

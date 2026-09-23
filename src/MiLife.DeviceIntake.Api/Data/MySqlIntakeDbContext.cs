using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MiLife.DeviceIntake.Api.Models;

namespace MiLife.DeviceIntake.Api.Data;

public sealed class MySqlIntakeDbContext(DbContextOptions<MySqlIntakeDbContext> options) : IntakeDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        // Preserve punctuation/case after explicit serial normalization, avoiding accent-insensitive matches.
        model.UseCollation("utf8mb4_bin");
        model.Entity<SupportJob>().Property(j => j.Output).HasColumnType("longtext");
        model.Entity<DeviceSubmission>(entity =>
        {
            entity.Property(s => s.ComputerName).HasMaxLength(256);
            entity.Property(s => s.Status).HasMaxLength(32);
            entity.Property(s => s.PayloadHash).HasMaxLength(64);
            entity.Property(s => s.CollectorVersion).HasMaxLength(32);
            entity.Property(s => s.SourceIp).HasMaxLength(64);
        });
        model.Entity<SubmissionReview>(entity =>
        {
            entity.Property(r => r.Status).HasMaxLength(32);
            entity.Property(r => r.PreviousStatus).HasMaxLength(32);
            entity.Property(r => r.Reviewer).HasMaxLength(256);
            entity.Property(r => r.Note).HasMaxLength(2000);
        });
    }
}

public sealed class MySqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MySqlIntakeDbContext>
{
    public MySqlIntakeDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<MySqlIntakeDbContext>()
        .UseMySql("Server=localhost;Database=milife_device_intake;User=design_only;Password=unused", new MySqlServerVersion(new Version(8, 4, 0))).Options);
}

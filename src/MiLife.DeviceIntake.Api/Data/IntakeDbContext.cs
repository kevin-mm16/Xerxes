using Microsoft.EntityFrameworkCore;
using MiLife.DeviceIntake.Api.Models;

namespace MiLife.DeviceIntake.Api.Data;

public class IntakeDbContext : DbContext
{
    public IntakeDbContext(DbContextOptions<IntakeDbContext> options) : base(options) { }
    protected IntakeDbContext(DbContextOptions options) : base(options) { }
    public DbSet<DeviceIdentity> Devices => Set<DeviceIdentity>();
    public DbSet<DeviceSubmission> DeviceSubmissions => Set<DeviceSubmission>();
    public DbSet<SubmissionReview> Reviews => Set<SubmissionReview>();
    public DbSet<DeviceAgent> Agents => Set<DeviceAgent>();
    public DbSet<SupportJob> SupportJobs => Set<SupportJob>();
    public DbSet<AgentAction> AgentActions => Set<AgentAction>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AgentAction>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Action).HasMaxLength(16);
            entity.Property(a => a.Status).HasMaxLength(32);
            entity.Property(a => a.RequestedBy).HasMaxLength(256);
            entity.Property(a => a.Error).HasMaxLength(300);
            entity.HasIndex(a => new { a.AgentId, a.RequestedAtUtc });
            entity.HasOne(a => a.Agent).WithMany().HasForeignKey(a => a.AgentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SupportJob>(entity =>
        {
            entity.HasKey(j => j.Id);
            entity.Property(j => j.Script).HasMaxLength(4096);
            entity.Property(j => j.CommandType).HasMaxLength(64);
            entity.Property(j => j.DisplayName).HasMaxLength(128);
            entity.Property(j => j.Output).HasMaxLength(16000);
            entity.Property(j => j.RequestedBy).HasMaxLength(256);
            entity.Property(j => j.Status).HasMaxLength(32);
            entity.HasIndex(j => new { j.AgentId, j.RequestedAtUtc });
            entity.HasOne(j => j.Agent).WithMany().HasForeignKey(j => j.AgentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceAgent>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.SerialNumber).HasMaxLength(128);
            entity.Property(a => a.CredentialHash).HasMaxLength(64);
            entity.Property(a => a.AgentVersion).HasMaxLength(32);
            entity.Property(a => a.EmployeeName).HasMaxLength(120);
            entity.Property(a => a.ConsentVersion).HasMaxLength(32);
            entity.HasIndex(a => new { a.SerialNumber, a.LastSeenAtUtc });
            entity.HasOne(a => a.Device).WithMany().HasForeignKey(a => a.SerialNumber).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceIdentity>(entity =>
        {
            entity.HasKey(d => d.SerialNumber);
            entity.Property(d => d.SerialNumber).HasMaxLength(128);
        });
        model.Entity<DeviceSubmission>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.CollectionId).IsUnique();
            entity.HasIndex(s => new { s.SerialNumber, s.ReceivedAtUtc });
            entity.HasIndex(s => new { s.Status, s.ReceivedAtUtc });
            entity.HasIndex(s => s.ComputerName);
            entity.HasIndex(s => s.BranchCode);
            entity.Property(s => s.DeviceType).HasMaxLength(16).HasDefaultValue("Unknown");
            entity.HasIndex(s => s.DeviceType);
            entity.Property(s => s.SerialNumber).HasMaxLength(128);
            entity.Property(s => s.BranchCode).HasMaxLength(32);
            entity.Property(s => s.Status).HasConversion<string>();
            entity.HasOne(s => s.Device).WithMany().HasForeignKey(s => s.SerialNumber).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SubmissionReview>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Status).HasConversion<string>();
            entity.Property(r => r.PreviousStatus).HasConversion<string>();
            entity.HasOne(r => r.Submission).WithMany().HasForeignKey(r => r.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

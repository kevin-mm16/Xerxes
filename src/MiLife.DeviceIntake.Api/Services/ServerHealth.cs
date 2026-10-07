using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MiLife.DeviceIntake.Api.Data;

namespace MiLife.DeviceIntake.Api.Services;

public static class ServerHealth
{
    public static async Task<IResult> ReadAsync(IntakeDbContext db, IWebHostEnvironment environment, IConfiguration configuration, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var databaseHealthy = false;
        try { _ = await db.DeviceSubmissions.AsNoTracking().AnyAsync(ct); databaseHealthy = true; }
        catch (Exception) when (!ct.IsCancellationRequested) { }
        watch.Stop();
        using var process = Process.GetCurrentProcess();
        long? diskTotal = null, diskFree = null, memoryTotal = null, memoryAvailable = null;
        double? loadOneMinute = null;
        DateTime? lastBackupUtc = null;
        var backupStatusPath = configuration["Operations:BackupStatusPath"];
        var backupStatus = string.IsNullOrWhiteSpace(backupStatusPath) ? "Not configured" : "Missing";
        try
        {
            var root = Path.GetPathRoot(environment.ContentRootPath)!;
            var drive = new DriveInfo(root);
            diskTotal = drive.TotalSize; diskFree = drive.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var memory = (await File.ReadAllLinesAsync("/proc/meminfo", ct))
                    .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    .Where(parts => parts.Length >= 2).ToDictionary(parts => parts[0], parts => parts[1]);
                if (long.TryParse(memory.GetValueOrDefault("MemTotal:"), out var total)) memoryTotal = total * 1024;
                if (long.TryParse(memory.GetValueOrDefault("MemAvailable:"), out var available)) memoryAvailable = available * 1024;
                var load = (await File.ReadAllTextAsync("/proc/loadavg", ct)).Split(' ')[0];
                if (double.TryParse(load, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) loadOneMinute = value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (!string.IsNullOrWhiteSpace(backupStatusPath))
        {
            try
            {
                var timestamp = (await File.ReadAllTextAsync(backupStatusPath, ct)).Trim();
                if (DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    lastBackupUtc = parsed;
                    backupStatus = DateTime.UtcNow - parsed <= TimeSpan.FromHours(36) ? "Healthy" : "Stale";
                }
                else backupStatus = "Invalid marker";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var status = !databaseHealthy ? "Degraded" : backupStatus is "Stale" or "Invalid marker" ? "Attention" : "Healthy";
        return Results.Ok(new
        {
            checkedAtUtc = DateTime.UtcNow, status,
            databaseHealthy, databaseLatencyMs = watch.ElapsedMilliseconds,
            apiUptimeSeconds = (long)(DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds,
            apiMemoryBytes = process.WorkingSet64, diskTotalBytes = diskTotal, diskFreeBytes = diskFree,
            memoryTotalBytes = memoryTotal, memoryAvailableBytes = memoryAvailable, loadOneMinute,
            backupStatus, lastBackupUtc
        });
    }
}

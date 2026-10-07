using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MiLife.DeviceContracts;
using MiLife.DeviceIntake.Api.Data;
using MiLife.DeviceIntake.Api.Models;
using MySqlConnector;

namespace MiLife.DeviceIntake.Api.Services;

public sealed class CollectionConflictException : Exception;

public sealed class SubmissionService(IntakeDbContext db, TimeProvider clock)
{
    public async Task<IntakeReceipt> ReceiveAsync(DeviceInventory data, string rawJson, string? sourceIp, CancellationToken ct)
    {
        // MySQL can race on a new serial or collection ID. Roll back completely, then re-read
        // the winning transaction to preserve repeat/idempotency semantics.
        for (var attempt = 0; ; attempt++)
        {
            try { return await ReceiveOnceAsync(data, rawJson, sourceIp, ct); }
            catch (Exception ex) when (attempt < 3 && IsMySqlConflict(ex))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), ct);
            }
        }
    }

    private static bool IsMySqlConflict(Exception ex) => ex is MySqlException { Number: 1062 or 1213 or 1205 }
        || ex is DbUpdateException { InnerException: MySqlException { Number: 1062 or 1213 or 1205 } };

    private async Task<IntakeReceipt> ReceiveOnceAsync(DeviceInventory data, string rawJson, string? sourceIp, CancellationToken ct)
    {
        // An immediate SQLite write transaction serializes identity creation and repeat detection.
        // All providers retain unique keys as the final concurrency safeguard.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var canonical = JsonSerializer.Serialize(data, InventoryJson.Options);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var previous = await db.DeviceSubmissions.SingleOrDefaultAsync(s => s.CollectionId == data.CollectionId, ct);
        if (previous is not null)
        {
            if (previous.PayloadHash != hash) throw new CollectionConflictException();
            return Receipt(previous);
        }
        var serial = InventoryValidation.NormalizeSerial(data.SerialNumber!);
        var device = await db.Devices.SingleOrDefaultAsync(d => d.SerialNumber == serial, ct);
        var isRepeat = device is not null;
        var now = clock.GetUtcNow().UtcDateTime;
        if (device is null)
        {
            device = new DeviceIdentity { SerialNumber = serial, CreatedAtUtc = now };
            db.Devices.Add(device);
        }
        var submission = new DeviceSubmission
        {
            Id = Guid.NewGuid(), CollectionId = data.CollectionId, PayloadHash = hash, SerialNumber = serial,
            Device = device, BranchCode = data.BranchCode.ToUpperInvariant(), ComputerName = data.ComputerName,
            LoggedInUser = data.LoggedInUser, Manufacturer = data.Manufacturer, Model = data.Model,
            DeviceType = data.DeviceType ?? "Unknown",
            ProcessorJson = JsonSerializer.Serialize(data.Processor, InventoryJson.Options), RamGB = data.Ram?.TotalGB,
            RamJson = JsonSerializer.Serialize(data.Ram, InventoryJson.Options), DisksJson = JsonSerializer.Serialize(data.Disks, InventoryJson.Options),
            WindowsJson = JsonSerializer.Serialize(data.Windows, InventoryJson.Options), NetworkJson = JsonSerializer.Serialize(data.NetworkAdapters, InventoryJson.Options),
            CollectorVersion = data.CollectorVersion, CollectedAtUtc = data.CollectedAtUtc.UtcDateTime,
            ReceivedAtUtc = now, SourceIp = sourceIp, Status = SubmissionStatus.PendingReview, IsRepeat = isRepeat, RawJson = rawJson
        };
        db.DeviceSubmissions.Add(submission);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Receipt(submission);
    }

    private static IntakeReceipt Receipt(DeviceSubmission submission) => new(true, submission.Id, submission.SerialNumber, submission.Status.ToString(), submission.IsRepeat);
}

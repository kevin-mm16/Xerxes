using Microsoft.EntityFrameworkCore;
using MiLife.DeviceContracts;
using MiLife.DeviceIntake.Api.Data;
using MiLife.DeviceIntake.Api.Models;

namespace MiLife.DeviceIntake.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdmin(this WebApplication app)
    {
        MapRoutes(app.MapGroup("/api/admin/submissions"));
        MapRoutes(app.MapGroup("/device-admin/api/submissions"));
    }

    private static void MapRoutes(RouteGroupBuilder group)
    {
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", async (Guid id, IntakeDbContext db, CancellationToken ct) =>
        {
            var submission = await db.DeviceSubmissions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
            if (submission is null) return Results.NotFound();
            var history = await db.Reviews.AsNoTracking().Where(r => r.SubmissionId == id).OrderBy(r => r.ReviewedAtUtc)
                .Select(r => new { r.Id, r.PreviousStatus, r.Status, r.Note, r.ReviewedAtUtc, r.Reviewer }).ToListAsync(ct);
            return Results.Ok(new { submission.Id, submission.SerialNumber, submission.Status, submission.IsRepeat,
                submission.ReceivedAtUtc, submission.SourceIp,
                hardware = System.Text.Json.JsonSerializer.Deserialize<DeviceInventory>(submission.RawJson, InventoryJson.Options), reviews = history });
        });
        group.MapPost("/{id:guid}/review", ReviewAsync);
    }

    private static async Task<IResult> ListAsync(IntakeDbContext db, string? serialNumber, string? computerName,
        string? branch, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        var pageNumber = page ?? 1;
        var size = pageSize ?? 25;
        if (pageNumber is < 1 or > 100_000 || size is < 1 or > 100 || serialNumber?.Length > 128
            || computerName?.Length > 256 || branch?.Length > 32) return Results.BadRequest(new { error = "Invalid search or pagination." });
        var query = db.DeviceSubmissions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            var serial = InventoryValidation.NormalizeSerial(serialNumber);
            query = query.Where(s => s.SerialNumber == serial);
        }
        if (!string.IsNullOrWhiteSpace(computerName)) query = query.Where(s => s.ComputerName != null && s.ComputerName.Contains(computerName));
        if (!string.IsNullOrWhiteSpace(branch))
        {
            var code = branch.Trim().ToUpperInvariant(); query = query.Where(s => s.BranchCode == code);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryStatus(status, out var state)) return Results.BadRequest(new { error = "Invalid status." });
            query = query.Where(s => s.Status == state);
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(s => s.ReceivedAtUtc).ThenBy(s => s.Id).Skip((pageNumber - 1) * size).Take(size)
            .Select(s => new { s.Id, s.SerialNumber, s.ComputerName, s.Manufacturer, s.Model, s.BranchCode,
                s.RamGB, s.Status, s.IsRepeat, s.CollectedAtUtc, s.ReceivedAtUtc }).ToListAsync(ct);
        return Results.Ok(new { total, page = pageNumber, pageSize = size, items });
    }

    private static async Task<IResult> ReviewAsync(Guid id, ReviewRequest request, IntakeDbContext db, TimeProvider clock, HttpContext context, CancellationToken ct)
    {
        if (!TryStatus(request.Status, out var next) || next == SubmissionStatus.PendingReview
            || !TryStatus(request.ExpectedStatus, out var expected) || request.Note?.Length > 2000
            || request.Note?.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t') == true)
            return Results.BadRequest(new { error = "Supply a valid status, expectedStatus, and an optional note up to 2000 characters." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var submission = await db.DeviceSubmissions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (submission is null) return Results.NotFound();
        if (submission.Status != expected) return Results.Conflict(new { error = "The submission status changed. Refresh before reviewing." });
        if (submission.Status == next) return Results.Ok(new { submission.Id, status = next.ToString() });
        var changed = await db.DeviceSubmissions.Where(s => s.Id == id && s.Status == expected)
            .ExecuteUpdateAsync(update => update.SetProperty(s => s.Status, next), ct);
        if (changed != 1) return Results.Conflict(new { error = "The submission status changed. Refresh before reviewing." });
        db.Reviews.Add(new SubmissionReview { Id = Guid.NewGuid(), SubmissionId = id, PreviousStatus = submission.Status,
            Status = next, Note = request.Note?.Trim(), ReviewedAtUtc = clock.GetUtcNow().UtcDateTime,
            Reviewer = context.User.Identity?.Name ?? "admin-token" });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { submission.Id, status = next.ToString() });
    }

    private static bool TryStatus(string? text, out SubmissionStatus status)
    {
        status = default;
        return text is not null && Enum.GetNames<SubmissionStatus>().Contains(text, StringComparer.OrdinalIgnoreCase)
            && Enum.TryParse(text, true, out status);
    }
}

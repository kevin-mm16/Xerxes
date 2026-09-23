using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MiLife.DeviceContracts;
using MiLife.DeviceIntake.Api.Data;
using MiLife.DeviceIntake.Api.Models;

namespace MiLife.DeviceIntake.Api.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgents(this WebApplication app)
    {
        app.MapPost("/api/device-agent/enroll", EnrollAsync).RequireRateLimiting("intake");
        app.MapPost("/api/device-agent/heartbeat", (AgentHeartbeat data, HttpContext context, IntakeDbContext db, TimeProvider clock, CancellationToken ct) =>
            UpdateAsync(data, context, db, clock, false, ct));
        app.MapPost("/api/device-agent/disable", (AgentHeartbeat data, HttpContext context, IntakeDbContext db, TimeProvider clock, CancellationToken ct) =>
            UpdateAsync(data, context, db, clock, true, ct));
        app.MapGet("/device-admin/api/agents", async (string serialNumber, IntakeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (serialNumber.Length > 128) return Results.BadRequest();
            var serial = InventoryValidation.NormalizeSerial(serialNumber);
            var cutoff = clock.GetUtcNow().UtcDateTime.AddMinutes(-15);
            var items = await db.Agents.AsNoTracking().Where(a => a.SerialNumber == serial).OrderByDescending(a => a.EnrolledAtUtc)
                .Select(a => new { a.Id, a.AgentVersion, a.EnrolledAtUtc, a.LastSeenAtUtc, a.IsRevoked, a.IsEnabled,
                    a.EmployeeName, a.Latitude, a.Longitude, a.AccuracyMeters, a.LocationCapturedAtUtc,
                    availability = a.IsRevoked || !a.IsEnabled ? "Disabled" : a.LastSeenAtUtc >= cutoff ? "Online" : "Offline" }).ToListAsync(ct);
            return Results.Ok(new { items });
        });
        app.MapPost("/device-admin/api/agents/{id:guid}/revoke", async (Guid id, IntakeDbContext db, HttpContext context, ILogger<DeviceAgent> logger, CancellationToken ct) =>
        {
            var count = await db.Agents.Where(a => a.Id == id).ExecuteUpdateAsync(update => update.SetProperty(a => a.IsRevoked, true).SetProperty(a => a.IsEnabled, false), ct);
            if (count == 0) return Results.NotFound();
            logger.LogInformation("Heartbeat agent {AgentId} revoked by {Reviewer}", id, context.User.Identity?.Name);
            return Results.Ok(new { revoked = true });
        });
    }

    private static bool ValidVersion(string? version) => version is { Length: > 0 and <= 32 } && !version.Any(char.IsControl);
    private static bool ValidToken(string? token) => token is { Length: 64 } && token.All(char.IsAsciiHexDigit);
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool Matches(string token, string hash) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(hash));

    private static async Task<IResult> EnrollAsync(AgentEnrollment data, IntakeDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if ((data.AgentVersion != "1.1.0" && string.IsNullOrWhiteSpace(data.EmployeeName))
            || data.EmployeeName is { Length: > 120 } || data.EmployeeName?.Any(char.IsControl) == true)
            return Results.BadRequest(new { error = "Employee name is required (maximum 120 characters)." });
        if (data.AgentId == Guid.Empty || !InventoryValidation.IsUsableSerial(data.SerialNumber) || !ValidVersion(data.AgentVersion) || !ValidToken(data.DeviceToken))
            return Results.BadRequest(new { error = "Invalid enrollment." });
        var serial = InventoryValidation.NormalizeSerial(data.SerialNumber);
        if (!await db.DeviceSubmissions.AnyAsync(s => s.Id == data.SubmissionId && s.SerialNumber == serial, ct))
            return Results.BadRequest(new { error = "Register inventory before enabling heartbeat." });
        var existing = await db.Agents.SingleOrDefaultAsync(a => a.Id == data.AgentId, ct);
        if (existing is not null)
        {
            if (existing.IsRevoked) return Results.StatusCode(403);
            if (await db.AgentActions.AnyAsync(a => a.AgentId == existing.Id && (a.Status == "Requested" || a.Status == "Acknowledged"), ct))
                return Results.Conflict(new { error = "A management action must finish before this agent can be enabled." });
            if (existing.SerialNumber != serial || !Matches(data.DeviceToken, existing.CredentialHash)) return Results.Conflict();
            existing.IsEnabled = true;
            if (!string.IsNullOrWhiteSpace(data.EmployeeName)) existing.EmployeeName = data.EmployeeName.Trim();
            existing.AgentVersion = data.AgentVersion;
        }
        else db.Agents.Add(new DeviceAgent { Id = data.AgentId, SerialNumber = serial, CredentialHash = Hash(data.DeviceToken),
            EmployeeName = data.EmployeeName?.Trim(), AgentVersion = data.AgentVersion, EnrolledAtUtc = clock.GetUtcNow().UtcDateTime });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Results.StatusCode(503); }
        return Results.Ok(new AgentReceipt(true, data.AgentId, 300));
    }

    private static async Task<IResult> UpdateAsync(AgentHeartbeat data, HttpContext context, IntakeDbContext db, TimeProvider clock, bool disable, CancellationToken ct)
    {
        if (data.AgentId == Guid.Empty || !ValidVersion(data.AgentVersion)) return Results.BadRequest();
        var agent = await AuthenticateAsync(data.AgentId, context, db, ct);
        if (agent is null) return Results.Unauthorized();
        if (agent.IsRevoked || !agent.IsEnabled) return Results.StatusCode(403);
        if (data.Location is { } location)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            if (!double.IsFinite(location.Latitude) || location.Latitude is < -90 or > 90
                || !double.IsFinite(location.Longitude) || location.Longitude is < -180 or > 180
                || !double.IsFinite(location.AccuracyMeters) || location.AccuracyMeters is < 0 or > 1000000
                || location.CapturedAtUtc < now.AddHours(-1) || location.CapturedAtUtc > now.AddMinutes(5)) return Results.BadRequest();
            await db.Agents.Where(a => a.Id == data.AgentId && a.IsEnabled && !a.IsRevoked)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Latitude, location.Latitude).SetProperty(a => a.Longitude, location.Longitude)
                    .SetProperty(a => a.AccuracyMeters, location.AccuracyMeters).SetProperty(a => a.LocationCapturedAtUtc, location.CapturedAtUtc), ct);
        }
        var updated = await db.Agents.Where(a => a.Id == data.AgentId && !a.IsRevoked && a.IsEnabled)
            .ExecuteUpdateAsync(update => update.SetProperty(a => a.LastSeenAtUtc, clock.GetUtcNow().UtcDateTime)
                .SetProperty(a => a.AgentVersion, data.AgentVersion).SetProperty(a => a.IsEnabled, !disable), ct);
        return updated == 1 ? Results.Ok(new { received = true, heartbeatIntervalSeconds = 300 }) : Results.StatusCode(403);
    }

    internal static async Task<DeviceAgent?> AuthenticateAsync(Guid id, HttpContext context, IntakeDbContext db, CancellationToken ct)
    {
        var headers = context.Request.Headers["X-Device-Token"];
        if (headers.Count != 1 || !ValidToken(headers[0])) return null;
        var agent = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        return agent is not null && Matches(headers[0]!, agent.CredentialHash) ? agent : null;
    }
}

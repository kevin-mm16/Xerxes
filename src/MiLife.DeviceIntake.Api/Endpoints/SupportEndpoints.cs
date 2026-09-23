using Microsoft.EntityFrameworkCore;
using MiLife.DeviceContracts;
using MiLife.DeviceIntake.Api.Data;
using MiLife.DeviceIntake.Api.Models;

namespace MiLife.DeviceIntake.Api.Endpoints;

public static class SupportEndpoints
{
    public static void MapSupport(this WebApplication app)
    {
        app.MapGet("/device-admin/api/agents/{id:guid}/commands", async (Guid id, IntakeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            await ExpireAsync(db, clock, ct);
            return Results.Ok(new { items = await db.SupportJobs.AsNoTracking().Where(j => j.AgentId == id)
                .OrderByDescending(j => j.RequestedAtUtc).Take(30).Select(j => new { j.Id, j.Script, j.RequestedBy, j.Status,
                    j.RequestedAtUtc, j.DecidedAtUtc, j.CompletedAtUtc, j.Output, j.ExitCode }).ToListAsync(ct) });
        });
        app.MapPost("/device-admin/api/agents/{id:guid}/commands", async (Guid id, SupportRequest request, HttpContext context, IntakeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Script) || request.Script.Length > 4096 || request.Script.Contains('\0')) return Results.BadRequest();
            var agent = await db.Agents.FindAsync([id], ct);
            if (agent is null) return Results.NotFound();
            if (!agent.IsEnabled || agent.IsRevoked || agent.AgentVersion == "1.1.0")
                return Results.BadRequest(new { error = "An enabled, updated agent is required." });
            await ExpireAsync(db, clock, ct);
            if (await db.SupportJobs.AnyAsync(j => j.AgentId == id && (j.Status == "Queued" || j.Status == "AwaitingApproval" || j.Status == "Running"), ct))
                return Results.Conflict(new { error = "A support request is already active." });
            var now = clock.GetUtcNow().UtcDateTime;
            var job = new SupportJob { Id = Guid.NewGuid(), AgentId = id, Script = request.Script,
                RequestedBy = context.User.Identity!.Name!, RequestedAtUtc = now, ExpiresAtUtc = now.AddMinutes(5) };
            db.SupportJobs.Add(job); await db.SaveChangesAsync(ct);
            return Results.Ok(new { job.Id, job.Status });
        });
        app.MapGet("/api/device-agent/{agentId:guid}/commands/next", async (Guid agentId, HttpContext context, IntakeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await Allowed(agentId, context, db, ct)) return Results.Unauthorized();
            await ExpireAsync(db, clock, ct);
            var job = await db.SupportJobs.AsNoTracking().Where(j => j.AgentId == agentId && j.Status == "Queued")
                .OrderBy(j => j.RequestedAtUtc).FirstOrDefaultAsync(ct);
            if (job is null) return Results.NoContent();
            var claimed = await db.SupportJobs.Where(j => j.Id == job.Id && j.Status == "Queued")
                .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, "AwaitingApproval"), ct);
            return claimed == 1 ? Results.Ok(new SupportCommand(job.Id, job.Script, job.RequestedBy, job.ExpiresAtUtc)) : Results.NoContent();
        });
        app.MapPost("/api/device-agent/commands/{id:guid}/decision", async (Guid id, SupportDecision decision, HttpContext context, IntakeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await Allowed(decision.AgentId, context, db, ct)) return Results.Unauthorized();
            var now = clock.GetUtcNow().UtcDateTime;
            var changed = await db.SupportJobs.Where(j => j.Id == id && j.AgentId == decision.AgentId && j.Status == "AwaitingApproval" && j.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, decision.Approved ? "Running" : "Declined")
                    .SetProperty(j => j.DecidedAtUtc, now), ct);
            return changed == 1 ? Results.Ok(new { accepted = true }) : Results.Conflict();
        });
        app.MapPost("/api/device-agent/commands/{id:guid}/result", async (Guid id, SupportResult result, HttpContext context, IntakeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await Allowed(result.AgentId, context, db, ct)) return Results.Unauthorized();
            if (result.Output is null || result.Output.Length > 16000) return Results.BadRequest();
            var changed = await db.SupportJobs.Where(j => j.Id == id && j.AgentId == result.AgentId && j.Status == "Running")
                .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, result.TimedOut ? "TimedOut" : "Completed")
                    .SetProperty(j => j.CompletedAtUtc, clock.GetUtcNow().UtcDateTime).SetProperty(j => j.Output, result.Output)
                    .SetProperty(j => j.ExitCode, result.ExitCode), ct);
            return changed == 1 ? Results.Ok(new { received = true }) : Results.Conflict();
        });
    }
    private static async Task<bool> Allowed(Guid id, HttpContext context, IntakeDbContext db, CancellationToken ct) =>
        await AgentEndpoints.AuthenticateAsync(id, context, db, ct) is { IsEnabled: true, IsRevoked: false };

    private static async Task ExpireAsync(IntakeDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.SupportJobs.Where(j => (j.Status == "Queued" || j.Status == "AwaitingApproval") && j.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, "Expired"), ct);
        await db.SupportJobs.Where(j => j.Status == "Running" && j.DecidedAtUtc < now.AddMinutes(-2))
            .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, "ResultUnavailable"), ct);
    }
}

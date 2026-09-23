using Microsoft.EntityFrameworkCore;
using MiLife.DeviceContracts;
using MiLife.DeviceIntake.Api.Data;
using MiLife.DeviceIntake.Api.Models;

namespace MiLife.DeviceIntake.Api.Endpoints;

public static class AgentActionEndpoints
{
    public static void MapAgentActions(this WebApplication app)
    {
        app.MapPost("/device-admin/api/agents/{id:guid}/enable", async (Guid id, IntakeDbContext db, HttpContext context, TimeProvider clock, CancellationToken ct) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var agent = await db.Agents.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (agent is null) return Results.NotFound();
            // Never restore credentials belonging to a removed installation or race its cleanup helper.
            if (await db.AgentActions.AnyAsync(a => a.AgentId == id && a.Action == "Remove"
                && (a.Status == "Requested" || a.Status == "Acknowledged" || a.Status == "Completed"), ct))
                return Results.Conflict(new { error = "Removal is pending or confirmed. A removed PC needs a fresh installation." });
            if (await db.AgentActions.AnyAsync(a => a.AgentId == id && a.Action == "Disable" && a.Status == "Acknowledged", ct))
                return Results.Conflict(new { error = "Wait for the PC to finish disabling before allowing re-enrollment." });
            if (agent.IsEnabled && !agent.IsRevoked) return Results.Ok(new { enabled = true });
            var now = clock.GetUtcNow().UtcDateTime;
            await db.AgentActions.Where(a => a.AgentId == id && a.Action == "Disable" && a.Status == "Requested")
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, "Cancelled").SetProperty(a => a.CompletedAtUtc, now), ct);
            agent.IsEnabled = true; agent.IsRevoked = false;
            db.AgentActions.Add(new AgentAction { Id = Guid.NewGuid(), AgentId = id, Action = "Enable", Status = "Completed",
                RequestedBy = context.User.Identity!.Name!, RequestedAtUtc = now, CompletedAtUtc = now });
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { enabled = true });
        });
        app.MapGet("/device-admin/api/agents/{id:guid}/actions", async (Guid id, IntakeDbContext db, CancellationToken ct) =>
            Results.Ok(new { items = await db.AgentActions.AsNoTracking().Where(a => a.AgentId == id)
                .OrderByDescending(a => a.RequestedAtUtc).Select(a => new { a.Id, a.Action, a.Status, a.RequestedBy,
                    a.RequestedAtUtc, a.AcknowledgedAtUtc, a.CompletedAtUtc, a.Error }).Take(30).ToListAsync(ct) }));
        app.MapPost("/device-admin/api/agents/{id:guid}/actions", async (Guid id, AgentActionRequest request, IntakeDbContext db, HttpContext context, TimeProvider clock, CancellationToken ct) =>
        {
            if (request.Action is not ("Disable" or "Remove")) return Results.BadRequest();
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var agent = await db.Agents.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (agent is null) return Results.NotFound();
            if (!Version.TryParse(agent.AgentVersion, out var version) || version < new Version(1, 3, 0))
                return Results.BadRequest(new { error = "Install agent 1.3 or later on this PC first." });
            if (agent.IsRevoked) return Results.Conflict(new { error = "This agent has been revoked or removed." });
            if (await db.AgentActions.AnyAsync(a => a.AgentId == id && (a.Status == "Requested" || a.Status == "Acknowledged"), ct))
                return Results.Conflict(new { error = "An action is already pending for this agent." });
            if (request.Action == "Disable" && !agent.IsEnabled) return Results.Conflict(new { error = "This agent is already disabled." });
            var action = new AgentAction { Id = Guid.NewGuid(), AgentId = id, Action = request.Action,
                RequestedBy = context.User.Identity!.Name!, RequestedAtUtc = clock.GetUtcNow().UtcDateTime };
            agent.IsEnabled = false; // Block further telemetry and support immediately; local cleanup requires acknowledgement.
            db.AgentActions.Add(action); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new AgentActionReceipt(action.Id, action.Action, action.Status));
        });
        // Disabled agents may retrieve ONLY their lifecycle action; this never returns PowerShell commands.
        app.MapGet("/api/device-agent/{agentId:guid}/action", async (Guid agentId, HttpContext context, IntakeDbContext db, CancellationToken ct) =>
        {
            if (await AgentEndpoints.AuthenticateAsync(agentId, context, db, ct) is not { IsRevoked: false }) return Results.Unauthorized();
            var action = await db.AgentActions.AsNoTracking().Where(a => a.AgentId == agentId && (a.Status == "Requested" || a.Status == "Acknowledged"))
                .OrderBy(a => a.RequestedAtUtc).FirstOrDefaultAsync(ct);
            return action is null ? Results.NoContent() : Results.Ok(new AgentActionReceipt(action.Id, action.Action, action.Status));
        });
        app.MapPost("/api/device-agent/actions/{id:guid}/status", async (Guid id, AgentActionUpdate update, HttpContext context, IntakeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var agent = await AgentEndpoints.AuthenticateAsync(update.AgentId, context, db, ct);
            if (agent is null) return Results.Unauthorized();
            if (update.Status is not ("Acknowledged" or "Completed" or "Failed") || update.Error?.Length > 300) return Results.BadRequest();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var action = await db.AgentActions.SingleOrDefaultAsync(a => a.Id == id && a.AgentId == update.AgentId, ct);
            if (action is null) return Results.NotFound();
            if (action.Status == update.Status) return Results.Ok(new { received = true });
            if (agent.IsRevoked || (update.Status == "Acknowledged" ? action.Status != "Requested" : action.Status != "Acknowledged")) return Results.Conflict();
            var now = clock.GetUtcNow().UtcDateTime;
            var changed = await db.AgentActions.Where(a => a.Id == id && a.AgentId == update.AgentId && a.Status == action.Status)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, update.Status)
                    .SetProperty(a => a.AcknowledgedAtUtc, update.Status == "Acknowledged" ? now : action.AcknowledgedAtUtc)
                    .SetProperty(a => a.CompletedAtUtc, update.Status == "Acknowledged" ? action.CompletedAtUtc : now)
                    .SetProperty(a => a.Error, update.Status == "Failed" ? update.Error : null), ct);
            if (changed != 1) return Results.Conflict();
            if (update.Status == "Completed" && action.Action == "Remove")
            { await db.Agents.Where(a => a.Id == update.AgentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsRevoked, true).SetProperty(a => a.IsEnabled, false), ct); }
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { received = true });
        });
    }
}

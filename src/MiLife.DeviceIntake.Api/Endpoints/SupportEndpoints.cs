using Microsoft.EntityFrameworkCore;
using MiLife.DeviceContracts;
using MiLife.DeviceIntake.Api.Data;
using MiLife.DeviceIntake.Api.Models;
using MiLife.DeviceIntake.Api.Services;

namespace MiLife.DeviceIntake.Api.Endpoints;

public static class SupportEndpoints
{
    public static void MapSupport(this WebApplication app)
    {
        app.MapGet("/device-admin/api/commands/catalog", Catalog);

        app.MapGet("/device-admin/api/agents/{id:guid}/commands", History);
        app.MapPost("/device-admin/api/agents/{id:guid}/commands", Queue);
        app.MapPost("/device-admin/api/agents/{agentId:guid}/commands/{id:guid}/cancel", Cancel);

        app.MapGet("/api/device-agent/{agentId:guid}/commands/next", Next);
        app.MapPost("/api/device-agent/commands/{id:guid}/decision", Decision);
        app.MapPost("/api/device-agent/commands/{id:guid}/result", Result);
    }

    private static IResult Catalog() => Results.Ok(new
    {
        items = SupportCommandCatalog.All.OrderBy(command => command.Category).ThenBy(command => command.Name)
            .Select(command => new { command.Id, command.Name, command.Description, command.Category, command.DefaultRunSilently })
    });

    private static async Task<IResult> History(Guid id, IntakeDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await ExpireAsync(db, clock, ct);
        return Results.Ok(new { items = await db.SupportJobs.AsNoTracking().Where(j => j.AgentId == id)
            .OrderByDescending(j => j.RequestedAtUtc).Take(50).Select(j => new { j.Id, j.CommandType, j.DisplayName,
                j.Script, j.RunSilently, j.RequestedBy, j.Status, j.RequestedAtUtc, j.ExpiresAtUtc,
                j.DecidedAtUtc, j.CompletedAtUtc, j.Output, j.ExitCode }).ToListAsync(ct) });
    }

    private static async Task<IResult> Queue(Guid id, SupportRequest request, HttpContext context, IntakeDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var hasScript = !string.IsNullOrWhiteSpace(request.Script);
        var hasTemplate = !string.IsNullOrWhiteSpace(request.TemplateId);
        if (hasScript == hasTemplate) return Results.BadRequest(new { error = "Choose one diagnostic action or supply one PowerShell command." });

        string script, commandType, displayName;
        if (hasTemplate)
        {
            if (!SupportCommandCatalog.TryGet(request.TemplateId, out var template))
                return Results.BadRequest(new { error = "Unknown diagnostic action." });
            script = template.Script; commandType = template.Id; displayName = template.Name;
        }
        else
        {
            script = request.Script!.Trim(); commandType = "custom"; displayName = "Custom PowerShell";
            if (script.Length > 4096 || script.Contains('\0'))
                return Results.BadRequest(new { error = "PowerShell must be between 1 and 4,096 characters." });
        }

        var agent = await db.Agents.FindAsync([id], ct);
        if (agent is null) return Results.NotFound();
        if (!agent.IsEnabled || agent.IsRevoked || agent.AgentVersion == "1.1.0")
            return Results.BadRequest(new { error = "An enabled, updated agent is required." });
        if (request.RunSilently && (!Version.TryParse(agent.AgentVersion, out var version) || version < new Version(1, 4, 0)
            || agent.ConsentVersion != AgentPolicy.PrivacyNoticeVersion || agent.ConsentAcceptedAtUtc is null))
            return Results.BadRequest(new { error = "Silent execution requires agent 1.4 or later and acceptance of the current management notice." });

        await ExpireAsync(db, clock, ct);
        if (await db.SupportJobs.AnyAsync(j => j.AgentId == id
            && (j.Status == "Queued" || j.Status == "AwaitingApproval" || j.Status == "Running"), ct))
            return Results.Conflict(new { error = "A support command is already active for this agent." });

        var now = clock.GetUtcNow().UtcDateTime;
        var job = new SupportJob { Id = Guid.NewGuid(), AgentId = id, Script = script, CommandType = commandType,
            DisplayName = displayName, RunSilently = request.RunSilently, RequestedBy = context.User.Identity!.Name!,
            RequestedAtUtc = now, ExpiresAtUtc = now.AddMinutes(5) };
        db.SupportJobs.Add(job); await db.SaveChangesAsync(ct);
        return Results.Ok(new { job.Id, job.Status, job.DisplayName, job.RunSilently, job.ExpiresAtUtc });
    }

    private static async Task<IResult> Cancel(Guid agentId, Guid id, IntakeDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var changed = await db.SupportJobs.Where(j => j.Id == id && j.AgentId == agentId
                && (j.Status == "Queued" || j.Status == "AwaitingApproval"))
            .ExecuteUpdateAsync(update => update.SetProperty(j => j.Status, "Cancelled")
                .SetProperty(j => j.CompletedAtUtc, clock.GetUtcNow().UtcDateTime), ct);
        return changed == 1 ? Results.Ok(new { cancelled = true })
            : Results.Conflict(new { error = "Only a queued or delivered command can be cancelled." });
    }

    private static async Task<IResult> Next(Guid agentId, HttpContext context, IntakeDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await Allowed(agentId, context, db, ct)) return Results.Unauthorized();
        await ExpireAsync(db, clock, ct);
        var job = await db.SupportJobs.AsNoTracking().Where(j => j.AgentId == agentId && j.Status == "Queued")
            .OrderBy(j => j.RequestedAtUtc).FirstOrDefaultAsync(ct);
        if (job is null) return Results.NoContent();
        var claimed = await db.SupportJobs.Where(j => j.Id == job.Id && j.Status == "Queued")
            .ExecuteUpdateAsync(update => update.SetProperty(j => j.Status, "AwaitingApproval"), ct);
        return claimed == 1
            ? Results.Ok(new SupportCommand(job.Id, job.Script, job.RequestedBy, job.ExpiresAtUtc, job.RunSilently, job.DisplayName))
            : Results.NoContent();
    }

    private static async Task<IResult> Decision(Guid id, SupportDecision decision, HttpContext context, IntakeDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        if (!await Allowed(decision.AgentId, context, db, ct)) return Results.Unauthorized();
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.SupportJobs.Where(j => j.Id == id && j.AgentId == decision.AgentId
                && j.Status == "AwaitingApproval" && j.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(update => update.SetProperty(j => j.Status, decision.Approved ? "Running" : "Declined")
                .SetProperty(j => j.DecidedAtUtc, now), ct);
        return changed == 1 ? Results.Ok(new { accepted = true }) : Results.Conflict();
    }

    private static async Task<IResult> Result(Guid id, SupportResult result, HttpContext context, IntakeDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        if (!await Allowed(result.AgentId, context, db, ct)) return Results.Unauthorized();
        if (result.Output is null || result.Output.Length > 16000) return Results.BadRequest();
        var changed = await db.SupportJobs.Where(j => j.Id == id && j.AgentId == result.AgentId && j.Status == "Running")
            .ExecuteUpdateAsync(update => update.SetProperty(j => j.Status, result.TimedOut ? "TimedOut" : "Completed")
                .SetProperty(j => j.CompletedAtUtc, clock.GetUtcNow().UtcDateTime).SetProperty(j => j.Output, result.Output)
                .SetProperty(j => j.ExitCode, result.ExitCode), ct);
        return changed == 1 ? Results.Ok(new { received = true }) : Results.Conflict();
    }

    private static async Task<bool> Allowed(Guid id, HttpContext context, IntakeDbContext db, CancellationToken ct) =>
        await AgentEndpoints.AuthenticateAsync(id, context, db, ct) is { IsEnabled: true, IsRevoked: false };

    private static async Task ExpireAsync(IntakeDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.SupportJobs.Where(j => (j.Status == "Queued" || j.Status == "AwaitingApproval") && j.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(update => update.SetProperty(j => j.Status, "Expired").SetProperty(j => j.CompletedAtUtc, now), ct);
        await db.SupportJobs.Where(j => j.Status == "Running" && j.DecidedAtUtc < now.AddMinutes(-2))
            .ExecuteUpdateAsync(update => update.SetProperty(j => j.Status, "ResultUnavailable").SetProperty(j => j.CompletedAtUtc, now), ct);
    }
}

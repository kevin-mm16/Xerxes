using System.Security.Claims;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MiLife.DeviceIntake.Api.Data;
using MiLife.DeviceIntake.Api.Models;
using MiLife.DeviceIntake.Api.Services;

namespace MiLife.DeviceIntake.Api.Endpoints;

public static class DashboardEndpoints
{
    public sealed record LoginRequest(string? Username, string? Password);

    public static void MapDashboard(this WebApplication app)
    {
        app.MapGet("/device-admin", (IWebHostEnvironment environment) => Results.File(
            Path.Combine(environment.ContentRootPath, "wwwroot", "device-admin", "index.html"), "text/html; charset=utf-8"));
        app.MapGet("/device-admin/api/session", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { authenticated = context.User.Identity?.IsAuthenticated == true,
                username = context.User.Identity?.Name, csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken }));
        app.MapPost("/device-admin/api/login", async (LoginRequest request, HttpContext context, DashboardCredentials credentials) =>
        {
            if (!credentials.IsConfigured) return Results.Problem("Dashboard login has not been configured.", statusCode: 503);
            if (!credentials.Validate(request.Username, request.Password)) return Results.Json(new { error = "Incorrect username or password." }, statusCode: 401);
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, credentials.Username),
                new Claim("credential-version", credentials.SessionVersion) }, CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
            { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4) });
            return Results.Ok(new { authenticated = true, username = credentials.Username });
        }).RequireRateLimiting("dashboard-login");
        app.MapPost("/device-admin/api/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { authenticated = false });
        });
        app.MapGet("/device-admin/api/devices", (IntakeDbContext db, string? search, string? status, int? page, CancellationToken ct) => ListDevicesAsync(db, search, status, page, ct));
        app.MapGet("/device-admin/api/devices/export", (IntakeDbContext db, string? search, string? status, CancellationToken ct) => ListDevicesAsync(db, search, status, null, ct, true));
    }

    private static async Task<IResult> ListDevicesAsync(IntakeDbContext db, string? search, string? status, int? page, CancellationToken ct, bool export = false)
    {
        var currentPage = page ?? 1;
        if (currentPage is < 1 or > 100000 || search?.Length > 256) return Results.BadRequest(new { error = "Invalid search or page." });
        // A scalar correlated subquery works on both SQLite and MySQL, including timestamp ties.
        var query = db.DeviceSubmissions.AsNoTracking().Where(s => s.Id == db.DeviceSubmissions
            .Where(other => other.SerialNumber == s.SerialNumber).OrderByDescending(other => other.ReceivedAtUtc)
            .ThenByDescending(other => other.Id).Select(other => other.Id).First());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(s => s.SerialNumber.Contains(term) || (s.ComputerName != null && s.ComputerName.ToUpper().Contains(term))
                || (s.LoggedInUser != null && s.LoggedInUser.ToUpper().Contains(term))
                || db.Agents.Any(a => a.SerialNumber == s.SerialNumber && a.EmployeeName != null && a.EmployeeName.ToUpper().Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.GetNames<SubmissionStatus>().Contains(status) || !Enum.TryParse<SubmissionStatus>(status, out var state))
                return Results.BadRequest(new { error = "Invalid status." });
            query = query.Where(s => s.Status == state);
        }
        var total = await query.CountAsync(ct);
        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        var projected = query.OrderByDescending(s => s.ReceivedAtUtc).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.SerialNumber, s.ComputerName, s.LoggedInUser, s.Manufacturer, s.Model, s.RamGB, s.Status,
                employeeName = db.Agents.Where(a => a.SerialNumber == s.SerialNumber && a.EmployeeName != null && !a.IsRevoked)
                    .OrderByDescending(a => a.EnrolledAtUtc).Select(a => a.EmployeeName).FirstOrDefault(),
                s.ReceivedAtUtc, s.BranchCode, submissionCount = db.DeviceSubmissions.Count(other => other.SerialNumber == s.SerialNumber),
                availability = db.Agents.Any(a => a.SerialNumber == s.SerialNumber && !a.IsRevoked && a.IsEnabled && a.LastSeenAtUtc >= cutoff) ? "Online"
                    : db.Agents.Any(a => a.SerialNumber == s.SerialNumber && !a.IsRevoked && a.IsEnabled) ? "Offline"
                    : db.Agents.Any(a => a.SerialNumber == s.SerialNumber) ? "Disabled" : "NotEnrolled",
                lastHeartbeatUtc = db.Agents.Where(a => a.SerialNumber == s.SerialNumber).Max(a => a.LastSeenAtUtc) });
        if (export)
        {
            return Results.Stream(async stream =>
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(true), 4096, leaveOpen: true);
                await writer.WriteLineAsync("Computer name,Serial number,Employee name,Windows user,Manufacturer,Model,RAM (GB),Review status,Availability,Last heartbeat (UTC),Last inventory (UTC),Branch,Submission count");
                await foreach (var item in projected.AsAsyncEnumerable().WithCancellation(ct))
                {
                    var cells = new object?[] { item.ComputerName, item.SerialNumber, item.employeeName, item.LoggedInUser,
                        item.Manufacturer, item.Model, item.RamGB, item.Status, item.availability, item.lastHeartbeatUtc?.ToString("O", CultureInfo.InvariantCulture),
                        item.ReceivedAtUtc.ToString("O", CultureInfo.InvariantCulture), item.BranchCode, item.submissionCount };
                    await writer.WriteLineAsync(string.Join(',', cells.Select(CsvCell)).AsMemory(), ct);
                }
            }, "text/csv; charset=utf-8", $"milife-devices-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        }
        var items = await projected.Skip((currentPage - 1) * 20).Take(20).ToListAsync(ct);
        return Results.Ok(new { items, total, page = currentPage, pageSize = 20, summary = new
        {
            devices = await db.Devices.CountAsync(ct), submissions = await db.DeviceSubmissions.CountAsync(ct),
            online = await db.Agents.Where(a => !a.IsRevoked && a.IsEnabled && a.LastSeenAtUtc >= cutoff).Select(a => a.SerialNumber).Distinct().CountAsync(ct),
            pending = await db.DeviceSubmissions.CountAsync(s => s.Status == SubmissionStatus.PendingReview, ct),
            approved = await db.DeviceSubmissions.CountAsync(s => s.Status == SubmissionStatus.Approved, ct)
        } });
    }

    private static string CsvCell(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        // Keep device-supplied text from becoming spreadsheet formulas, including after whitespace.
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n')) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

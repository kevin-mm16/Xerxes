using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiLife.DeviceIntake.Api.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MiLife.DeviceContracts;
using MiLife.DeviceIntake.Api.Services;

namespace MiLife.DeviceIntake.Api.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly TestApi factory = new();
    public static DeviceInventory Payload(string serial = "TEST-123") => new()
    {
        CollectionId = Guid.NewGuid(), SerialNumber = serial, ComputerName = "TEST-PC",
        CollectorVersion = "1.0.0", CollectedAtUtc = DateTimeOffset.UtcNow
    };
    private HttpClient Client(bool admin = false)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(admin ? "X-Admin-Token" : "X-Enrollment-Token", admin ? TestApi.AdminToken : TestApi.IntakeToken);
        return client;
    }
    private static Task<HttpResponseMessage> Send(HttpClient client, DeviceInventory data) => client.PostAsJsonAsync("/api/device-intake", data, InventoryJson.Options);

    [Fact]
    public async Task SuccessAndRepeatedDevicePreserveHistory()
    {
        using var client = Client();
        using var first = await Send(client, Payload(" test-123 "));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var receipt = await first.Content.ReadFromJsonAsync<IntakeReceipt>(InventoryJson.Options);
        Assert.Equal("PendingReview", receipt!.Status); Assert.Equal("TEST-123", receipt.SerialNumber); Assert.False(receipt.IsRepeat);
        using var second = await Send(client, Payload());
        var repeat = await second.Content.ReadFromJsonAsync<IntakeReceipt>(InventoryJson.Options);
        Assert.True(repeat!.IsRepeat); Assert.NotEqual(receipt.SubmissionId, repeat.SubmissionId);
        using var admin = Client(true);
        using var list = await admin.GetAsync("/api/admin/submissions?serialNumber=test-123&status=PendingReview");
        var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TransportRetryReturnsSameSubmissionAndConflictingIdIsRejected()
    {
        using var client = Client(); var data = Payload();
        using var first = await Send(client, data); using var second = await Send(client, data);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        using var conflict = await Send(client, data with { ComputerName = "DIFFERENT" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var admin = Client(true);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/submissions");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ConcurrentSubmissionsAssociateOneDevice()
    {
        using var client = Client();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Send(client, Payload())));
        var receipts = new List<IntakeReceipt>();
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                receipts.Add((await response.Content.ReadFromJsonAsync<IntakeReceipt>(InventoryJson.Options))!);
            }
        }
        Assert.Equal(1, receipts.Count(r => !r.IsRepeat));
        Assert.Equal(4, receipts.Select(r => r.SubmissionId).Distinct().Count());
    }

    [Theory]
    [InlineData(null)] [InlineData("wrong-token")]
    public async Task MissingOrInvalidTokenIsUnauthorized(string? token)
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        if (token is not null) client.DefaultRequestHeaders.Add("X-Enrollment-Token", token);
        using var response = await Send(client, Payload()); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EnrollmentTokenCannotReview()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Add("X-Admin-Token", TestApi.IntakeToken);
        using var response = await client.GetAsync("/api/admin/submissions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"serialNumber\":\"A\",\"serialNumber\":\"B\"}")]
    [InlineData("{\"passwords\":[]}")]
    [InlineData("{\"collectionId\":\"not-a-guid\"}")]
    public async Task InvalidJsonIsRejected(string json)
    {
        using var client = Client();
        using var response = await client.PostAsync("/api/device-intake", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("Unknown")]
    public async Task MissingSerialIsRejected(string? serial)
    {
        using var client = Client();
        using var response = await Send(client, Payload() with { SerialNumber = serial });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NullArrayElementsReturnBadRequestInsteadOfCrashing()
    {
        using var client = Client();
        using var response = await Send(client, Payload() with { Disks = [null!] });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OversizedBodyIsRejected()
    {
        using var client = Client();
        using var response = await client.PostAsync("/api/device-intake", new StringContent(new string(' ', 140000), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task ReviewPersistsAuditAndDetectsStaleStatus()
    {
        using var client = Client(); using var response = await Send(client, Payload());
        var receipt = (await response.Content.ReadFromJsonAsync<IntakeReceipt>(InventoryJson.Options))!;
        using var admin = Client(true);
        using var review = await admin.PostAsJsonAsync($"/api/admin/submissions/{receipt.SubmissionId}/review", new { status = "Approved", expectedStatus = "PendingReview", note = "Checked with IT" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/submissions/{receipt.SubmissionId}");
        Assert.Equal("Approved", detail.GetProperty("status").GetString());
        Assert.Equal(1, detail.GetProperty("reviews").GetArrayLength());
        using var stale = await admin.PostAsJsonAsync($"/api/admin/submissions/{receipt.SubmissionId}/review", new { status = "Rejected", expectedStatus = "PendingReview" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task HttpAndSpoofedProxyHeadersAreRejected()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost") });
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        using var response = await client.GetAsync("/device-registration");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RateLimitReturns429AndRetryAfter()
    {
        using var limited = new TestApi(2); using var client = limited.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Enrollment-Token", TestApi.IntakeToken);
        using var first = await Send(client, Payload()); using var second = await Send(client, Payload()); using var third = await Send(client, Payload());
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);
    }

    [Fact]
    public async Task DownloadUsesConfiguredFileAndHealthHasNoSecrets()
    {
        using var client = Client();
        using var response = await client.GetAsync("/download/device-collector");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("MiLifeDeviceRegistration.exe", response.Content.Headers.ContentDisposition.ToString());
        Assert.Equal("fixture", await response.Content.ReadAsStringAsync());
        Assert.Equal("{\"status\":\"healthy\"}", await client.GetStringAsync("/health"));
        Assert.Contains("Click to download", await client.GetStringAsync("/device-registration"));
    }

    public void Dispose() => factory.Dispose();

    private async Task<HttpClient> DashboardClient()
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var initial = await client.GetFromJsonAsync<JsonElement>("/device-admin/api/session");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", initial.GetProperty("csrfToken").GetString());
        using var login = await client.PostAsJsonAsync("/device-admin/api/login", new { username = "root", password = TestApi.DashboardPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        var session = await client.GetFromJsonAsync<JsonElement>("/device-admin/api/session");
        Assert.True(session.GetProperty("authenticated").GetBoolean());
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        return client;
    }

    [Theory]
    [InlineData("/device-admin")]
    [InlineData("/device-admin/")]
    public async Task DashboardPageIsServedWithAndWithoutTrailingSlash(string path)
    {
        using var client = Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("login-form", await response.Content.ReadAsStringAsync());
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task DashboardRequiresLoginAndCsrfAndListsUniqueDevices()
    {
        using var intake = Client();
        using var first = await Send(intake, Payload()); using var second = await Send(intake, Payload());
        using var denied = await intake.GetAsync("/device-admin/api/devices"); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var admin = await DashboardClient();
        var devices = await admin.GetFromJsonAsync<JsonElement>("/device-admin/api/devices");
        Assert.Equal(1, devices.GetProperty("total").GetInt32());
        Assert.Equal(2, devices.GetProperty("items")[0].GetProperty("submissionCount").GetInt32());
        Assert.Equal("NotEnrolled", devices.GetProperty("items")[0].GetProperty("availability").GetString());
        var id = devices.GetProperty("items")[0].GetProperty("id").GetString();
        using var review = await admin.PostAsJsonAsync($"/device-admin/api/submissions/{id}/review", new { status = "Approved", expectedStatus = "PendingReview" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/device-admin/api/submissions/{id}");
        Assert.Equal("root", detail.GetProperty("reviews")[0].GetProperty("reviewer").GetString());
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var csrf = await admin.PostAsJsonAsync($"/device-admin/api/submissions/{id}/review", new { status = "Rejected", expectedStatus = "Approved" });
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
    }

    [Fact]
    public async Task DashboardLoginRejectsWrongPasswordAndMissingCsrf()
    {
        using var client = Client();
        using var noCsrf = await client.PostAsJsonAsync("/device-admin/api/login", new { username = "root", password = TestApi.DashboardPassword });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        var session = await client.GetFromJsonAsync<JsonElement>("/device-admin/api/session");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var wrong = await client.PostAsJsonAsync("/device-admin/api/login", new { username = "root", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task AgentEnrollmentHeartbeatDisableAndRevokeAreAuthenticated()
    {
        using var intake = Client(); var inventory = Payload();
        using var submitted = await Send(intake, inventory);
        var receipt = (await submitted.Content.ReadFromJsonAsync<IntakeReceipt>(InventoryJson.Options))!;
        var enrollment = new AgentEnrollment(Guid.NewGuid(), receipt.SubmissionId, inventory.SerialNumber!, "1.1.0", new string('a',64));
        using var enrolled = await intake.PostAsJsonAsync("/api/device-agent/enroll", enrollment);
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        using var duplicate = await intake.PostAsJsonAsync("/api/device-agent/enroll", enrollment);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var hijack = await intake.PostAsJsonAsync("/api/device-agent/enroll", enrollment with { DeviceToken = new string('b',64) });
        Assert.Equal(HttpStatusCode.Conflict, hijack.StatusCode);
        var heartbeat = new AgentHeartbeat(enrollment.AgentId, "1.1.0");
        using var missingToken = await intake.PostAsJsonAsync("/api/device-agent/heartbeat", heartbeat);
        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        intake.DefaultRequestHeaders.Add("X-Device-Token", enrollment.DeviceToken);
        using var beat = await intake.PostAsJsonAsync("/api/device-agent/heartbeat", heartbeat);
        Assert.Equal(HttpStatusCode.OK, beat.StatusCode);
        using var admin = await DashboardClient();
        var devices = await admin.GetFromJsonAsync<JsonElement>("/device-admin/api/devices");
        Assert.Equal("Online", devices.GetProperty("items")[0].GetProperty("availability").GetString());
        using var disabled = await intake.PostAsJsonAsync("/api/device-agent/disable", heartbeat);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        using var whileDisabled = await intake.PostAsJsonAsync("/api/device-agent/heartbeat", heartbeat);
        Assert.Equal(HttpStatusCode.Forbidden, whileDisabled.StatusCode);
        using var reenabled = await intake.PostAsJsonAsync("/api/device-agent/enroll", enrollment);
        Assert.Equal(HttpStatusCode.OK, reenabled.StatusCode);
        using var revoked = await admin.PostAsJsonAsync($"/device-admin/api/agents/{enrollment.AgentId}/revoke", new { });
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var afterRevoke = await intake.PostAsJsonAsync("/api/device-agent/heartbeat", heartbeat);
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.StatusCode);
        using var forbiddenReenable = await intake.PostAsJsonAsync("/api/device-agent/enroll", enrollment);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenReenable.StatusCode);
    }

    private async Task<(HttpClient Client, AgentEnrollment Enrollment)> ManagedAgent(string serial = "MANAGED-1")
    {
        var client = Client();
        using var submitted = await Send(client, Payload(serial));
        var receipt = (await submitted.Content.ReadFromJsonAsync<IntakeReceipt>(InventoryJson.Options))!;
        var enrollment = new AgentEnrollment(Guid.NewGuid(), receipt.SubmissionId, serial, "1.2.0", new string('c', 64), "Sales Test Person");
        using var response = await client.PostAsJsonAsync("/api/device-agent/enroll", enrollment);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Add("X-Device-Token", enrollment.DeviceToken);
        return (client, enrollment);
    }

    [Fact]
    public async Task RequiredNameAndLocationAreValidatedAndSearchable()
    {
        var (device, enrollment) = await ManagedAgent(); using var owner = device;
        using var missing = await device.PostAsJsonAsync("/api/device-agent/enroll", enrollment with { EmployeeName = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var location = new DeviceLocation(5.6, -0.2, 300, DateTime.UtcNow);
        using var invalid = await device.PostAsJsonAsync("/api/device-agent/heartbeat", new AgentHeartbeat(enrollment.AgentId, "1.2.0", location with { Latitude = 91 }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var stale = await device.PostAsJsonAsync("/api/device-agent/heartbeat", new AgentHeartbeat(enrollment.AgentId, "1.2.0", location with { CapturedAtUtc = DateTime.UtcNow.AddDays(-1) }));
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        using var valid = await device.PostAsJsonAsync("/api/device-agent/heartbeat", new AgentHeartbeat(enrollment.AgentId, "1.2.0", location));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var admin = await DashboardClient();
        var list = await admin.GetFromJsonAsync<JsonElement>("/device-admin/api/devices?search=Sales%20Test");
        Assert.Equal("Sales Test Person", list.GetProperty("items")[0].GetProperty("employeeName").GetString());
        var agents = await admin.GetFromJsonAsync<JsonElement>("/device-admin/api/agents?serialNumber=MANAGED-1");
        Assert.Equal(5.6, agents.GetProperty("items")[0].GetProperty("latitude").GetDouble());
        // A heartbeat without a fix must not fabricate or erase the last reported location.
        using var unavailable = await device.PostAsJsonAsync("/api/device-agent/heartbeat", new AgentHeartbeat(enrollment.AgentId, "1.2.0"));
        Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SupportRequiresOwnerApprovalAndKeepsAudit(bool approve)
    {
        var (device, enrollment) = await ManagedAgent(); using var owner = device;
        using var outsider = Client(); using var admin = await DashboardClient();
        var route = $"/device-admin/api/agents/{enrollment.AgentId}/commands";
        using var denied = await outsider.PostAsJsonAsync(route, new SupportRequest("Get-Date"));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var created = await admin.PostAsJsonAsync(route, new SupportRequest("Get-Date"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var duplicate = await admin.PostAsJsonAsync(route, new SupportRequest("Get-Date"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var earlyResult = await device.PostAsJsonAsync($"/api/device-agent/commands/{id}/result", new SupportResult(enrollment.AgentId, "test", 0, false));
        Assert.Equal(HttpStatusCode.Conflict, earlyResult.StatusCode);
        var next = $"/api/device-agent/{enrollment.AgentId}/commands/next";
        using var noToken = await outsider.GetAsync(next); Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        var command = await device.GetFromJsonAsync<SupportCommand>(next, InventoryJson.Options);
        Assert.Equal(id, command!.Id); Assert.Equal("root", command.RequestedBy);
        using var reclaimed = await device.GetAsync(next); Assert.Equal(HttpStatusCode.NoContent, reclaimed.StatusCode);
        using var wrongOwner = await device.PostAsJsonAsync($"/api/device-agent/commands/{id}/decision", new SupportDecision(Guid.NewGuid(), true));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongOwner.StatusCode);
        using var decision = await device.PostAsJsonAsync($"/api/device-agent/commands/{id}/decision", new SupportDecision(enrollment.AgentId, approve));
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        using var repeated = await device.PostAsJsonAsync($"/api/device-agent/commands/{id}/decision", new SupportDecision(enrollment.AgentId, true));
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        using var result = await device.PostAsJsonAsync($"/api/device-agent/commands/{id}/result", new SupportResult(enrollment.AgentId, "test output", 0, false));
        Assert.Equal(approve ? HttpStatusCode.OK : HttpStatusCode.Conflict, result.StatusCode);
        var history = await admin.GetFromJsonAsync<JsonElement>(route);
        Assert.Equal(approve ? "Completed" : "Declined", history.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal("root", history.GetProperty("items")[0].GetProperty("requestedBy").GetString());
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var csrf = await admin.PostAsJsonAsync(route, new SupportRequest("Get-Date"));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
    }

    [Fact]
    public async Task ExpiredAndRevokedSupportCannotRun()
    {
        var (device, enrollment) = await ManagedAgent(); using var owner = device; using var admin = await DashboardClient();
        var route = $"/device-admin/api/agents/{enrollment.AgentId}/commands";
        using var created = await admin.PostAsJsonAsync(route, new SupportRequest("Get-Date"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await device.GetFromJsonAsync<SupportCommand>($"/api/device-agent/{enrollment.AgentId}/commands/next", InventoryJson.Options);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IntakeDbContext>().SupportJobs.Where(j => j.Id == id)
                .ExecuteUpdateAsync(u => u.SetProperty(j => j.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        using var approval = await device.PostAsJsonAsync($"/api/device-agent/commands/{id}/decision", new SupportDecision(enrollment.AgentId, true));
        Assert.Equal(HttpStatusCode.Conflict, approval.StatusCode);
        var history = await admin.GetFromJsonAsync<JsonElement>(route);
        Assert.Equal("Expired", history.GetProperty("items")[0].GetProperty("status").GetString());
        using var next = await admin.PostAsJsonAsync(route, new SupportRequest("Get-Date")); Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        var nextId = (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await device.GetFromJsonAsync<SupportCommand>($"/api/device-agent/{enrollment.AgentId}/commands/next", InventoryJson.Options);
        using var revoke = await admin.PostAsJsonAsync($"/device-admin/api/agents/{enrollment.AgentId}/revoke", new { });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        using var revokedApproval = await device.PostAsJsonAsync($"/api/device-agent/commands/{nextId}/decision", new SupportDecision(enrollment.AgentId, true));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedApproval.StatusCode);
    }
    [Fact]
    public async Task CsvExportRequiresLoginIncludesAllPagesAndUsesFiltersAndSafeCells()
    {
        using var intake = Client();
        using var denied = await intake.GetAsync("/device-admin/api/devices/export");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        for (var i = 0; i < 22; i++)
        {
            using var submitted = await Send(intake, Payload("EXPORT-" + i) with { ComputerName = i == 0 ? "=SUM(1,2)" : "Export PC", Model = "Model \"Quoted\"" });
            Assert.True(submitted.IsSuccessStatusCode);
        }
        using var admin = await DashboardClient();
        using var response = await admin.GetAsync("/device-admin/api/devices/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Equal(23, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"'=SUM(1,2)\"", csv);
        Assert.Contains("\"Model \"\"Quoted\"\"\"", csv);
        var filtered = await admin.GetStringAsync("/device-admin/api/devices/export?search=EXPORT-21");
        Assert.Equal(2, filtered.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("EXPORT-21", filtered);
        var empty = await admin.GetStringAsync("/device-admin/api/devices/export?status=Approved");
        Assert.Single(empty.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var invalid = await admin.GetAsync("/device-admin/api/devices/export?status=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    [Theory]
    [InlineData("Disable")]
    [InlineData("Remove")]
    public async Task LifecycleActionsRequireAuthAndAcknowledgementAndPreserveRecords(string action)
    {
        var (device,enrollment) = await ManagedAgent(); using var owner=device;
        using var admin=await DashboardClient(); using var outsider=Client();
        var route=$"/device-admin/api/agents/{enrollment.AgentId}/actions";
        using var legacy=await admin.PostAsJsonAsync(route,new AgentActionRequest(action));
        Assert.Equal(HttpStatusCode.BadRequest,legacy.StatusCode);
        enrollment=enrollment with { AgentVersion="1.3.0" };
        using var upgraded=await device.PostAsJsonAsync("/api/device-agent/enroll",enrollment);Assert.Equal(HttpStatusCode.OK,upgraded.StatusCode);
        using var denied=await outsider.PostAsJsonAsync(route,new AgentActionRequest(action));Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using var created=await admin.PostAsJsonAsync(route,new AgentActionRequest(action));Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var receipt=(await created.Content.ReadFromJsonAsync<AgentActionReceipt>(InventoryJson.Options))!;
        Assert.Equal("Requested",receipt.Status);
        using var duplicate=await admin.PostAsJsonAsync(route,new AgentActionRequest(action));Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
        using var telemetry=await device.PostAsJsonAsync("/api/device-agent/heartbeat",new AgentHeartbeat(enrollment.AgentId,"1.3.0"));Assert.Equal(HttpStatusCode.Forbidden,telemetry.StatusCode);
        using var pendingEnable=await device.PostAsJsonAsync("/api/device-agent/enroll",enrollment);Assert.Equal(HttpStatusCode.Conflict,pendingEnable.StatusCode);
        var pending=await device.GetFromJsonAsync<AgentActionReceipt>($"/api/device-agent/{enrollment.AgentId}/action",InventoryJson.Options);Assert.Equal(receipt.Id,pending!.Id);
        var statusRoute=$"/api/device-agent/actions/{receipt.Id}/status";
        using var wrong=await device.PostAsJsonAsync(statusRoute,new AgentActionUpdate(Guid.NewGuid(),"Acknowledged"));Assert.Equal(HttpStatusCode.Unauthorized,wrong.StatusCode);
        using var premature=await device.PostAsJsonAsync(statusRoute,new AgentActionUpdate(enrollment.AgentId,"Completed"));Assert.Equal(HttpStatusCode.Conflict,premature.StatusCode);
        using var ack=await device.PostAsJsonAsync(statusRoute,new AgentActionUpdate(enrollment.AgentId,"Acknowledged"));Assert.Equal(HttpStatusCode.OK,ack.StatusCode);
        using var complete=await device.PostAsJsonAsync(statusRoute,new AgentActionUpdate(enrollment.AgentId,"Completed"));Assert.Equal(HttpStatusCode.OK,complete.StatusCode);
        using var retry=await device.PostAsJsonAsync(statusRoute,new AgentActionUpdate(enrollment.AgentId,"Completed"));Assert.Equal(HttpStatusCode.OK,retry.StatusCode);
        using var enabled=await device.PostAsJsonAsync("/api/device-agent/enroll",enrollment);
        Assert.Equal(action=="Remove"?HttpStatusCode.Forbidden:HttpStatusCode.OK,enabled.StatusCode);
        var history=await admin.GetFromJsonAsync<JsonElement>(route);Assert.Equal("Completed",history.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal("root",history.GetProperty("items")[0].GetProperty("requestedBy").GetString());
        var devices=await admin.GetFromJsonAsync<JsonElement>("/device-admin/api/devices");Assert.Equal(1,devices.GetProperty("total").GetInt32());
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");using var csrf=await admin.PostAsJsonAsync(route,new AgentActionRequest("Remove"));Assert.Equal(HttpStatusCode.BadRequest,csrf.StatusCode);
    }
    [Fact]
    public async Task AdministratorCanRestoreLegacyRevokedAgentButNotRemovedInstallation()
    {
        var (device,enrollment)=await ManagedAgent(); using var owner=device; using var admin=await DashboardClient();
        var enableRoute=$"/device-admin/api/agents/{enrollment.AgentId}/enable";
        using var revoke=await admin.PostAsJsonAsync($"/device-admin/api/agents/{enrollment.AgentId}/revoke",new{});
        using var blocked=await device.PostAsJsonAsync("/api/device-agent/enroll",enrollment); Assert.Equal(HttpStatusCode.Forbidden,blocked.StatusCode);
        using var unauthorized=await device.PostAsJsonAsync(enableRoute,new{}); Assert.Equal(HttpStatusCode.Unauthorized,unauthorized.StatusCode);
        using var restored=await admin.PostAsJsonAsync(enableRoute,new{}); Assert.Equal(HttpStatusCode.OK,restored.StatusCode);
        using var enroll=await device.PostAsJsonAsync("/api/device-agent/enroll",enrollment); Assert.Equal(HttpStatusCode.OK,enroll.StatusCode);
        var history=await admin.GetFromJsonAsync<JsonElement>($"/device-admin/api/agents/{enrollment.AgentId}/actions");
        Assert.Equal("Enable",history.GetProperty("items")[0].GetProperty("action").GetString());
        Assert.Equal("root",history.GetProperty("items")[0].GetProperty("requestedBy").GetString());
        using(var scope=factory.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<IntakeDbContext>();
            db.AgentActions.Add(new MiLife.DeviceIntake.Api.Models.AgentAction{Id=Guid.NewGuid(),AgentId=enrollment.AgentId,Action="Remove",Status="Completed",RequestedBy="test",RequestedAtUtc=DateTime.UtcNow});
            await db.SaveChangesAsync();
        }
        using var removed=await admin.PostAsJsonAsync(enableRoute,new{}); Assert.Equal(HttpStatusCode.Conflict,removed.StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var noCsrf=await admin.PostAsJsonAsync(enableRoute,new{});Assert.Equal(HttpStatusCode.BadRequest,noCsrf.StatusCode);
    }

    [Fact]
    public async Task RestoreCancelsUndeliveredDisableAndRejectsItsLateAcknowledgement()
    {
        var (device,enrollment)=await ManagedAgent();using var owner=device;using var admin=await DashboardClient();
        using var upgraded=await device.PostAsJsonAsync("/api/device-agent/enroll",enrollment with {AgentVersion="1.3.0"});
        using var disable=await admin.PostAsJsonAsync($"/device-admin/api/agents/{enrollment.AgentId}/actions",new AgentActionRequest("Disable"));
        var action=(await disable.Content.ReadFromJsonAsync<AgentActionReceipt>(InventoryJson.Options))!;
        using var enabled=await admin.PostAsJsonAsync($"/device-admin/api/agents/{enrollment.AgentId}/enable",new{});Assert.Equal(HttpStatusCode.OK,enabled.StatusCode);
        using var lateAck=await device.PostAsJsonAsync($"/api/device-agent/actions/{action.Id}/status",new AgentActionUpdate(enrollment.AgentId,"Acknowledged"));Assert.Equal(HttpStatusCode.Conflict,lateAck.StatusCode);
        using var next=await device.GetAsync($"/api/device-agent/{enrollment.AgentId}/action");Assert.Equal(HttpStatusCode.NoContent,next.StatusCode);
    }
    private sealed class TestApi(int limit = 100) : WebApplicationFactory<Program>
    {
        public const string IntakeToken = "test-intake-token-12345678901234567890";
        public const string AdminToken = "test-admin-token-123456789012345678901";
        public const string DashboardPassword = "TestDashboardPassword123!";
        private static readonly string DashboardHash = DashboardCredentials.HashPassword(DashboardPassword);
        private readonly string directory = Path.Combine(Path.GetTempPath(), "milife-api-tests-" + Guid.NewGuid().ToString("N"));
        protected override IHost CreateHost(IHostBuilder builder)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "collector.exe"), "fixture");
            builder.UseEnvironment("Testing");
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DEVICE_INTAKE_TOKEN"] = IntakeToken, ["DEVICE_ADMIN_TOKEN"] = AdminToken,
                ["DEVICE_ADMIN_USERNAME"] = "root", ["DEVICE_ADMIN_PASSWORD_HASH"] = DashboardHash,
                ["Admin:DataProtectionPath"] = Path.Combine(directory,"keys"),
                ["ConnectionStrings:DeviceIntake"] = $"Data Source={Path.Combine(directory, "test.db")};Pooling=False;Default Timeout=10",
                ["Intake:CollectorPath"] = Path.Combine(directory, "collector.exe"),
                ["Intake:AgentPath"] = Path.Combine(directory, "collector.exe"),
                ["Intake:RequestsPerMinute"] = limit.ToString(), ["Intake:GlobalRequestsPerMinute"] = "10000",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
            return base.CreateHost(builder);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

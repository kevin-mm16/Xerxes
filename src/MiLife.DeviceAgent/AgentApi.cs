using System.Net;
using System.Net.Http.Json;
using MiLife.DeviceCollector.Configuration;
using MiLife.DeviceCollector.Diagnostics;
using MiLife.DeviceCollector.Hardware;
using MiLife.DeviceCollector.Submission;
using MiLife.DeviceContracts;

namespace MiLife.DeviceAgent;

public sealed class AgentApi(CollectorSettings settings, LocalFiles logs) : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 65536 };

    private string Endpoint(string path) => new Uri(settings.ApiUrl).GetLeftPart(UriPartial.Authority) + "/api/device-agent/" + path;
    public string ActionStatusUrl(Guid id) => Endpoint($"actions/{id}/status");

    public async Task<AgentActionReceipt?> PendingActionAsync(AgentProfile profile)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint($"{profile.AgentId}/action"));
        request.Headers.Add("X-Device-Token", profile.DeviceToken);
        using var response = await http.SendAsync(request);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentActionReceipt>(InventoryJson.Options);
    }
    public async Task<bool> ActionStatusAsync(AgentProfile profile, Guid id, string status, string? error = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ActionStatusUrl(id));
        request.Headers.Add("X-Device-Token", profile.DeviceToken);
        request.Content = JsonContent.Create(new AgentActionUpdate(profile.AgentId, status, error), options: InventoryJson.Options);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    public async Task CollectAsync(AgentProfile profile, ProfileStore store)
    {
        if (!settings.IsConfigured) throw new InvalidOperationException("This agent has not been configured by IT.");
        if (profile.SubmissionId == Guid.Empty)
        {
            var inventory = profile.PendingInventory ?? (await Task.Run(() => new WindowsHardwareCollector(settings, logs).Collect("UNKNOWN"))) with { CollectionId = profile.CollectionId };
            profile.PendingInventory = inventory; store.Save(profile);
            var result = await new SubmissionClient(http, settings, logs, new RetryDelay()).SubmitAsync(inventory);
            if (!result.Success || result.Receipt is null) throw new InvalidOperationException(result.Message);
            profile.SubmissionId = result.Receipt.SubmissionId; profile.SerialNumber = result.Receipt.SerialNumber;
            profile.PendingInventory = null;
            store.Save(profile);
        }
    }

    public async Task EnableAsync(AgentProfile profile, ProfileStore store)
    {
        await CollectAsync(profile, store);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("enroll"));
        request.Headers.Add("X-Enrollment-Token", settings.EnrollmentToken);
        request.Content = JsonContent.Create(new AgentEnrollment(profile.AgentId, profile.SubmissionId, profile.SerialNumber, "1.3.1", profile.DeviceToken, profile.EmployeeName), options: InventoryJson.Options);
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("IT has blocked this installation. Ask IT to restore access in the dashboard, then try Check in again.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Heartbeat enrollment failed. Please contact IT or try again later.");
        var receipt = await response.Content.ReadFromJsonAsync<AgentReceipt>(InventoryJson.Options);
        if (receipt is not { Registered: true } || receipt.AgentId != profile.AgentId) throw new InvalidOperationException("The enrollment response was not valid.");
    }

    public async Task<HttpStatusCode> SendAsync(AgentProfile profile, bool disable = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(disable ? "disable" : "heartbeat"));
        request.Headers.Add("X-Device-Token", profile.DeviceToken);
        var location = disable ? null : await WindowsLocation.ReadAsync();
        request.Content = JsonContent.Create(new AgentHeartbeat(profile.AgentId, "1.3.1", location), options: InventoryJson.Options);
        using var response = await http.SendAsync(request);
        logs.Log("HeartbeatStatus", ((int)response.StatusCode).ToString());
        return response.StatusCode;
    }
    public async Task<SupportCommand?> NextCommandAsync(AgentProfile profile)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint($"{profile.AgentId}/commands/next"));
        request.Headers.Add("X-Device-Token", profile.DeviceToken);
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SupportCommand>(InventoryJson.Options);
    }
    public async Task<bool> CommandUpdateAsync(AgentProfile profile, Guid id, string action, object data)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint($"commands/{id}/{action}"));
        request.Headers.Add("X-Device-Token", profile.DeviceToken);
        request.Content = JsonContent.Create(data, options: InventoryJson.Options);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }
    public void Dispose() => http.Dispose();
}

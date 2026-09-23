using System.Net;
using System.Net.Http.Json;
using MiLife.DeviceCollector.Configuration;
using MiLife.DeviceCollector.Diagnostics;
using MiLife.DeviceContracts;

namespace MiLife.DeviceCollector.Submission;

public sealed record SubmissionResult(bool Success, string Message, IntakeReceipt? Receipt = null);
public interface IRetryDelay { Task WaitAsync(TimeSpan delay, CancellationToken ct); }
public sealed class RetryDelay : IRetryDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}

public sealed class SubmissionClient(HttpClient http, CollectorSettings settings, LocalFiles files, IRetryDelay delay)
{
    public async Task<SubmissionResult> SubmitAsync(DeviceInventory inventory, CancellationToken ct = default)
    {
        if (!settings.IsConfigured) return new(false, "This collector has not been configured correctly. Contact IT.");
        var lastMessage = "The server could not be reached.";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var retryDelay = TimeSpan.FromSeconds(attempt * 2);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl);
                request.Headers.Add("X-Enrollment-Token", settings.EnrollmentToken);
                request.Content = JsonContent.Create(inventory, options: InventoryJson.Options);
                using var response = await http.SendAsync(request, ct);
                files.Log("ApiStatus", ((int)response.StatusCode).ToString());
                if (response.IsSuccessStatusCode)
                {
                    IntakeReceipt? receipt;
                    try { receipt = await response.Content.ReadFromJsonAsync<IntakeReceipt>(InventoryJson.Options, ct); }
                    catch (System.Text.Json.JsonException) { receipt = null; }
                    if (receipt is { Received: true } && receipt.SubmissionId != Guid.Empty
                        && receipt.SerialNumber == InventoryValidation.NormalizeSerial(inventory.SerialNumber ?? ""))
                        return new(true, "Device information submitted successfully.", receipt);
                    lastMessage = "The server returned an unexpected response. Contact IT.";
                }
                else
                {
                    lastMessage = response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Enrollment was not accepted. Contact IT for an updated collector.",
                        HttpStatusCode.BadRequest => "The server could not accept this device information. Contact IT.",
                        HttpStatusCode.TooManyRequests => "The server is busy. Please try again later.",
                        _ => "The server could not accept the submission. Contact IT."
                    };
                    if (response.StatusCode != HttpStatusCode.RequestTimeout && response.StatusCode != HttpStatusCode.TooManyRequests
                        && (int)response.StatusCode < 500) return new(false, lastMessage);
                    var retryAfter = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                    if (retryAfter > TimeSpan.FromSeconds(30)) return new(false, lastMessage);
                    if (retryAfter > retryDelay) retryDelay = retryAfter.Value;
                }
            }
            catch (HttpRequestException ex) { files.Log("NetworkFailure", ex.GetType().Name); lastMessage = "The server could not be reached. Check your connection or contact IT."; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { files.Log("RequestTimeout"); lastMessage = "The connection to the server timed out."; }
            if (attempt < 3) await delay.WaitAsync(retryDelay, ct);
        }
        return new(false, lastMessage);
    }
}

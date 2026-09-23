using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MiLife.DeviceContracts;

namespace MiLife.DeviceAgent;

internal static class LocalPowerShell
{
    // Use the Windows-supplied executable, the current token and normal execution policy.
    public static async Task<(string Output, int? ExitCode, bool TimedOut)> RunAsync(string script, int seconds)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-STA", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell could not start.");
        var output = ReadBoundedAsync(process.StandardOutput); var error = ReadBoundedAsync(process.StandardError);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var timedOut = false;
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { timedOut = true; try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        // Descendants may inherit output handles: do not block forever waiting for EOF.
        var combined = "";
        try { combined = (await output.WaitAsync(TimeSpan.FromSeconds(3))) + (await error.WaitAsync(TimeSpan.FromSeconds(3))); }
        catch (TimeoutException) { combined = "Output unavailable: an output stream remained open."; }
        return (combined.Length > 16000 ? combined[..15965] + "\n[Output truncated]" : combined, process.HasExited ? process.ExitCode : null, timedOut);
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var result = new StringBuilder(); var buffer = new char[1024]; int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
            if (result.Length < 16000) result.Append(buffer, 0, Math.Min(count, 16000 - result.Length));
        return result.ToString();
    }
}

internal static class WindowsLocation
{
    // Uses Windows' existing location permission. No IP-geolocation service or permission bypass.
    public static async Task<DeviceLocation?> ReadAsync()
    {
        const string script = """
            Add-Type -AssemblyName System.Device
            $watcher = New-Object System.Device.Location.GeoCoordinateWatcher
            try {
                if ($watcher.TryStart($true, [TimeSpan]::FromSeconds(6)) -and !$watcher.Position.Location.IsUnknown) {
                    $point = $watcher.Position.Location
                    @{latitude=$point.Latitude;longitude=$point.Longitude;accuracyMeters=$point.HorizontalAccuracy;capturedAtUtc=$watcher.Position.Timestamp.UtcDateTime.ToString('o')} | ConvertTo-Json -Compress
                }
            } finally { $watcher.Dispose() }
            """;
        try
        {
            var result = await LocalPowerShell.RunAsync(script, 10);
            if (result.ExitCode != 0 || result.TimedOut || string.IsNullOrWhiteSpace(result.Output)) return null;
            var location = JsonSerializer.Deserialize<DeviceLocation>(result.Output, InventoryJson.Options);
            return location is not null && double.IsFinite(location.AccuracyMeters) && location.AccuracyMeters >= 0
                && location.CapturedAtUtc > DateTime.UtcNow.AddHours(-1) ? location : null;
        }
        catch (Exception) { return null; }
    }
}

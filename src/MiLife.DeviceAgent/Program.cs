using System.Reflection;
using System.Text.Json;
using MiLife.DeviceCollector.Configuration;

namespace MiLife.DeviceAgent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ProfileStore.AcceptanceTest = args.Contains("--acceptance-test");
        var cleanupIndex = Array.IndexOf(args, "--cleanup-source");
        using var mutex = new Mutex(false, "Local\\MiLifeDeviceHeartbeat-" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value + (ProfileStore.AcceptanceTest ? "-AcceptanceTest" : ""));
        bool acquired;
        try { acquired = mutex.WaitOne(cleanupIndex >= 0 ? 15000 : 0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { MessageBox.Show("MiLife is already running. Open it from the notification area.", "MiLife device agent"); return; }
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AgentSettings");
            var settings = resource is null ? new CollectorSettings() : JsonSerializer.Deserialize<CollectorSettings>(resource) ?? new();
            if (cleanupIndex >= 0 && cleanupIndex + 1 < args.Length) _ = CleanupInstallerAsync(args[cleanupIndex + 1]);
            Application.Run(new AgentForm(settings, args.Contains("--background")));
        }
        catch (Exception)
        { MessageBox.Show("The heartbeat agent could not start. Please contact IT. No administrator access is required.", "MiLife Device Heartbeat", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { mutex.ReleaseMutex(); }
    }

    private static async Task CleanupInstallerAsync(string source)
    {
        try
        {
            var target = Path.GetFullPath(source);
            var current = Path.GetFullPath(Environment.ProcessPath!);
            if (target.Equals(current, StringComparison.OrdinalIgnoreCase) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(target) || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) return;
            // Delete only the identical installer binary, never a directory or unrelated executable.
            var expected = System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(current));
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    var actual = System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(target));
                    if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected)) return;
                    File.Delete(target); return;
                }
                catch (IOException) { await Task.Delay(500); }
            }
        }
        catch (Exception) { /* Cleanup is best effort; keep the registered agent running. */ }
    }
}

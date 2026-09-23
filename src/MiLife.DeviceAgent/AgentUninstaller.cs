using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace MiLife.DeviceAgent;

internal static class AgentUninstaller
{
    public static async Task StartAsync(ProfileStore store)
    {
        var expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProfileStore.FolderName));
        if (!Path.GetFullPath(store.DirectoryPath).Equals(expected, StringComparison.OrdinalIgnoreCase)
            || (File.GetAttributes(expected) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The agent directory is not safe to remove.");
        var marker = Path.Combine(expected, "uninstall-ready");
        if (File.Exists(marker)) File.Delete(marker);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("UninstallScript")!;
        using var reader = new StreamReader(resource);
        var script = "$agentParentPid = " + Environment.ProcessId + "\r\n$acceptanceTest = " + (ProfileStore.AcceptanceTest ? "$true" : "$false") + "\r\n" + await reader.ReadToEndAsync();
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The removal helper could not start.");
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(marker)) return;
            if (process.HasExited) break;
            await Task.Delay(100);
        }
        if (!process.HasExited) process.Kill();
        throw new InvalidOperationException("The removal helper did not become ready.");
    }
}

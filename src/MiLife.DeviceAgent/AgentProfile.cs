using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace MiLife.DeviceAgent;

public sealed class AgentProfile
{
    public Guid AgentId { get; set; } = Guid.NewGuid();
    public string DeviceToken { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public Guid SubmissionId { get; set; }
    public string SerialNumber { get; set; } = "";
    public bool Enabled { get; set; }
    public string EmployeeName { get; set; } = "";
    public string ManagementNoticeVersion { get; set; } = "";
    public Guid CollectionId { get; set; } = Guid.NewGuid();
    public MiLife.DeviceContracts.DeviceInventory? PendingInventory { get; set; }
    public Guid RemovalActionId { get; set; }
    public string? RemovalApiUrl { get; set; }
}

public sealed class ProfileStore
{
    internal static bool AcceptanceTest { get; set; }
    internal static string FolderName => AcceptanceTest ? "MiLifeDeviceAgent.AcceptanceTest" : "MiLifeDeviceAgent";
    internal static string ShortcutName => AcceptanceTest ? "MiLife Device Heartbeat Acceptance Test.lnk" : "MiLife Device Heartbeat.lnk";
    public string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);
    private string ProfilePath => Path.Combine(DirectoryPath, "profile.dat");
    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);
    public AgentProfile? Load() => !File.Exists(ProfilePath) ? null : JsonSerializer.Deserialize<AgentProfile>(
        ProtectedData.Unprotect(File.ReadAllBytes(ProfilePath), null, DataProtectionScope.CurrentUser));
    public void Save(AgentProfile profile)
    {
        Directory.CreateDirectory(DirectoryPath);
        var encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(profile), null, DataProtectionScope.CurrentUser);
        var temporary = ProfilePath + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, ProfilePath, true);
    }
    public void EnableStartup()
    {
        Directory.CreateDirectory(DirectoryPath);
        var target = Path.Combine(DirectoryPath, "MiLifeDeviceAgent.exe");
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to locate the agent executable.");
        if (!Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(source, target, true);
        // Create a normal current-user Startup shortcut. No service, registry edit, shell command or elevation.
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcuts are unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(ShortcutPath);
        try
        {
            shortcut.TargetPath = target; shortcut.Arguments = AcceptanceTest ? "--background --acceptance-test" : "--background";
            shortcut.WorkingDirectory = DirectoryPath; shortcut.Description = "MiLife visible device heartbeat";
            shortcut.Save();
        }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }
    public void DisableStartup() { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); }
}

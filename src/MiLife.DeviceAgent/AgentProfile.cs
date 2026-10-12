using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace MiLife.DeviceAgent;

public sealed class AgentProfile
{
    public Guid AgentId { get; set; } = Guid.NewGuid();
    public string DeviceToken { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public Guid SubmissionId { get; set; }
    public int InventorySchemaVersion { get; set; }
    public string SerialNumber { get; set; } = "";
    public bool Enabled { get; set; }
    public string EmployeeName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string ManagementNoticeVersion { get; set; } = "";
    public string PrivacyNoticeVersion { get; set; } = "";
    public DateTime? PrivacyAcceptedAtUtc { get; set; }
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
    internal static string TaskName => AcceptanceTest ? "MiLife Device Heartbeat Acceptance Test" : "MiLife Device Heartbeat";
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
        RegisterTask(target);
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }
    private static void RegisterTask(string target)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type)!; dynamic? folder = null; dynamic? definition = null;
        try
        {
            service.Connect(); folder = service.GetFolder("\\"); definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "MiLife company device inventory and IT support agent";
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            definition.Principal.UserId = identity;
            definition.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
            definition.Principal.RunLevel = 0; // TASK_RUNLEVEL_LUA; never request elevation
            definition.Settings.Enabled = true; definition.Settings.StartWhenAvailable = true;
            definition.Settings.DisallowStartIfOnBatteries = false; definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.MultipleInstances = 2; definition.Settings.ExecutionTimeLimit = "PT0S";
            definition.Settings.RestartCount = 3; definition.Settings.RestartInterval = "PT1M";
            dynamic trigger = definition.Triggers.Create(9); // TASK_TRIGGER_LOGON
            trigger.UserId = identity;
            dynamic action = definition.Actions.Create(0); // TASK_ACTION_EXEC
            action.Path = target; action.Arguments = AcceptanceTest ? "--background --acceptance-test" : "--background";
            action.WorkingDirectory = Path.GetDirectoryName(target);
            folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, 3, null);
            Marshal.FinalReleaseComObject(action); Marshal.FinalReleaseComObject(trigger);
        }
        finally
        {
            if (definition is not null) Marshal.FinalReleaseComObject(definition);
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            Marshal.FinalReleaseComObject(service);
        }
    }
    public void DisableStartup()
    {
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is not null)
            {
                dynamic service = Activator.CreateInstance(type)!; service.Connect(); dynamic folder = service.GetFolder("\\");
                try { folder.DeleteTask(TaskName, 0); } catch (COMException ex) when ((uint)ex.HResult == 0x80070002) { }
                Marshal.FinalReleaseComObject(folder); Marshal.FinalReleaseComObject(service);
            }
        }
        finally { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); }
    }
}

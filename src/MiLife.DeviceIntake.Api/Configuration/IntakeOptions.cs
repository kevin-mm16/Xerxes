namespace MiLife.DeviceIntake.Api.Configuration;

public sealed class IntakeOptions
{
    public string CollectorPath { get; set; } = "downloads/MiLifeDeviceCollector.exe";
    public string AgentPath { get; set; } = "downloads/MiLifeDeviceAgent.exe";
    public string PreviewAgentPath { get; set; } = "downloads/MiLifeDeviceAgentPreview.exe";
    public bool AllowInsecureLocalDevelopment { get; set; }
    public int RequestLimitBytes { get; set; } = 131072;
    public int RequestsPerMinute { get; set; } = 20;
    public int GlobalRequestsPerMinute { get; set; } = 300;
}

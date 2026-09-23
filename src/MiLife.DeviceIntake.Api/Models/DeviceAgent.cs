namespace MiLife.DeviceIntake.Api.Models;

public sealed class DeviceAgent
{
    public Guid Id { get; set; }
    public string SerialNumber { get; set; } = "";
    public DeviceIdentity Device { get; set; } = null!;
    public string CredentialHash { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public DateTime EnrolledAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsRevoked { get; set; }
    public string? EmployeeName { get; set; }
    public string? ConsentVersion { get; set; }
    public DateTime? ConsentAcceptedAtUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public DateTime? LocationCapturedAtUtc { get; set; }
}

public sealed class SupportJob
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public DeviceAgent Agent { get; set; } = null!;
    public string Script { get; set; } = "";
    public string CommandType { get; set; } = "custom";
    public string DisplayName { get; set; } = "Custom PowerShell";
    public bool RunSilently { get; set; }
    public string RequestedBy { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Output { get; set; }
    public int? ExitCode { get; set; }
}

public sealed class AgentAction
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public DeviceAgent Agent { get; set; } = null!;
    public string Action { get; set; } = "";
    public string Status { get; set; } = "Requested";
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Error { get; set; }
}

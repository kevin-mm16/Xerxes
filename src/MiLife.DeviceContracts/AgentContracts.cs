namespace MiLife.DeviceContracts;

public static class AgentPolicy
{
    public const string AgentVersion = "1.5.0";
    public const string PrivacyNoticeVersion = "2026-09-23";
}

public sealed record AgentEnrollment(Guid AgentId, Guid SubmissionId, string SerialNumber, string AgentVersion, string DeviceToken,
    string? EmployeeName = null, string? ConsentVersion = null, DateTime? ConsentAcceptedAtUtc = null);
public sealed record AgentHeartbeat(Guid AgentId, string AgentVersion, DeviceLocation? Location = null);
public sealed record AgentReceipt(bool Registered, Guid AgentId, int HeartbeatIntervalSeconds);
public sealed record DeviceLocation(double Latitude, double Longitude, double AccuracyMeters, DateTime CapturedAtUtc);
public sealed record SupportRequest(string? Script = null, string? TemplateId = null, bool RunSilently = false);
public sealed record SupportCommand(Guid Id, string Script, string RequestedBy, DateTime ExpiresAtUtc,
    bool RunSilently = false, string? DisplayName = null);
public sealed record SupportDecision(Guid AgentId, bool Approved);
public sealed record SupportResult(Guid AgentId, string Output, int? ExitCode, bool TimedOut);
public sealed record AgentActionRequest(string Action);
public sealed record AgentActionReceipt(Guid Id, string Action, string Status);
public sealed record AgentActionUpdate(Guid AgentId, string Status, string? Error = null);

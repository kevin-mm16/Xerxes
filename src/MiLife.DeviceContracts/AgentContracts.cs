namespace MiLife.DeviceContracts;

public sealed record AgentEnrollment(Guid AgentId, Guid SubmissionId, string SerialNumber, string AgentVersion, string DeviceToken, string? EmployeeName = null);
public sealed record AgentHeartbeat(Guid AgentId, string AgentVersion, DeviceLocation? Location = null);
public sealed record AgentReceipt(bool Registered, Guid AgentId, int HeartbeatIntervalSeconds);
public sealed record DeviceLocation(double Latitude, double Longitude, double AccuracyMeters, DateTime CapturedAtUtc);
public sealed record SupportRequest(string Script);
public sealed record SupportCommand(Guid Id, string Script, string RequestedBy, DateTime ExpiresAtUtc);
public sealed record SupportDecision(Guid AgentId, bool Approved);
public sealed record SupportResult(Guid AgentId, string Output, int? ExitCode, bool TimedOut);
public sealed record AgentActionRequest(string Action);
public sealed record AgentActionReceipt(Guid Id, string Action, string Status);
public sealed record AgentActionUpdate(Guid AgentId, string Status, string? Error = null);

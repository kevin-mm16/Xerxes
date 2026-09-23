namespace MiLife.DeviceIntake.Api.Models;

public enum SubmissionStatus { PendingReview, Matched, Approved, Rejected }

public sealed class DeviceIdentity
{
    public string SerialNumber { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class DeviceSubmission
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public DeviceIdentity Device { get; set; } = null!;
    public string BranchCode { get; set; } = "UNKNOWN";
    public string? ComputerName { get; set; }
    public string? LoggedInUser { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? ProcessorJson { get; set; }
    public double? RamGB { get; set; }
    public string? RamJson { get; set; }
    public string? DisksJson { get; set; }
    public string? WindowsJson { get; set; }
    public string? NetworkJson { get; set; }
    public string CollectorVersion { get; set; } = "";
    public DateTime CollectedAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string? SourceIp { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.PendingReview;
    public bool IsRepeat { get; set; }
    public string RawJson { get; set; } = "";
}

public sealed class SubmissionReview
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public DeviceSubmission Submission { get; set; } = null!;
    public SubmissionStatus PreviousStatus { get; set; }
    public SubmissionStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
    public string Reviewer { get; set; } = "admin-token";
}

public sealed record ReviewRequest(string Status, string ExpectedStatus, string? Note);

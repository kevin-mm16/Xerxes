namespace MiLife.DeviceContracts;

public sealed record DeviceInventory
{
    public Guid CollectionId { get; init; }
    public string CollectorVersion { get; init; } = "";
    public string BranchCode { get; init; } = "UNKNOWN";
    public string? ComputerName { get; init; }
    public string? LoggedInUser { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public ProcessorInfo? Processor { get; init; }
    public MemoryInfo? Ram { get; init; }
    public List<DiskInfo>? Disks { get; init; } = [];
    public WindowsInfo? Windows { get; init; }
    public List<NetworkAdapterInfo>? NetworkAdapters { get; init; } = [];
    public DateTimeOffset CollectedAtUtc { get; init; }
}

public sealed record ProcessorInfo(string? Manufacturer, string? Name, int? PhysicalCores, int? LogicalProcessors);
public sealed record MemoryInfo(double? TotalGB, List<MemoryModuleInfo>? Modules);
public sealed record MemoryModuleInfo(double? CapacityGB, string? Manufacturer, string? PartNumber, int? SpeedMHz);
public sealed record DiskInfo(string? Model, string? SerialNumber, double? CapacityGB, string? MediaType, string? BusType);
public sealed record WindowsInfo(string? Edition, string? Version, string? BuildNumber, string? Architecture);
public sealed record NetworkAdapterInfo(string? Name, string? MacAddress);
public sealed record IntakeReceipt(bool Received, Guid SubmissionId, string SerialNumber, string Status, bool IsRepeat);

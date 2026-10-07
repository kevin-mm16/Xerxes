using System.Text.RegularExpressions;

namespace MiLife.DeviceContracts;

public static partial class InventoryValidation
{
    public static string NormalizeSerial(string serial) => serial.Trim().ToUpperInvariant();

    private static readonly HashSet<string> PlaceholderSerials = new(StringComparer.OrdinalIgnoreCase)
    {
        "UNKNOWN", "NONE", "N/A", "NA", "DEFAULT STRING", "SYSTEM SERIAL NUMBER",
        "TO BE FILLED BY O.E.M.", "TO BE FILLED BY OEM", "NOT SPECIFIED", "NOT AVAILABLE"
    };

    public static bool IsUsableSerial(string? serial) => !string.IsNullOrWhiteSpace(serial)
        && serial.Trim().Length <= 128 && !serial.Any(char.IsControl)
        && !PlaceholderSerials.Contains(serial.Trim())
        && !serial.Trim().All(c => c is '0' or '-');

    public static List<string> Validate(DeviceInventory data, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (data.CollectionId == Guid.Empty) errors.Add("collectionId is required.");
        if (!IsUsableSerial(data.SerialNumber)) errors.Add("A usable BIOS serial number is required.");
        if (string.IsNullOrWhiteSpace(data.CollectorVersion)) errors.Add("collectorVersion is required.");
        if (data.BranchCode is null || !BranchPattern().IsMatch(data.BranchCode)) errors.Add("Invalid branchCode.");
        if (data.CollectedAtUtc == default || data.CollectedAtUtc > now.AddMinutes(10)) errors.Add("Invalid collectedAtUtc.");
        void Text(string? value, string field, int max = 256)
        {
            if (value is not null && (value.Length > max || value.Any(char.IsControl))) errors.Add($"Invalid {field}.");
        }
        void Capacity(double? value, string field)
        {
            if (value is not null && (!double.IsFinite(value.Value) || value < 0 || value > 100_000_000)) errors.Add($"Invalid {field}.");
        }
        Text(data.CollectorVersion, "collectorVersion", 32);
        Text(data.ComputerName, "computerName"); Text(data.LoggedInUser, "loggedInUser");
        Text(data.Manufacturer, "manufacturer"); Text(data.Model, "model");
        if (data.DeviceType is not (null or "Desktop" or "Laptop" or "Unknown")) errors.Add("Invalid deviceType.");
        if (data.Processor is { } cpu)
        {
            Text(cpu.Manufacturer, "processor.manufacturer"); Text(cpu.Name, "processor.name");
            if (cpu.PhysicalCores is < 1 or > 65536 || cpu.LogicalProcessors is < 1 or > 65536)
                errors.Add("Invalid processor counts.");
        }
        Capacity(data.Ram?.TotalGB, "ram.totalGB");
        if (data.Ram?.Modules is { } modules)
        {
            if (modules.Count > 256) errors.Add("Too many RAM modules.");
            foreach (var module in modules)
            {
                if (module is null) { errors.Add("Invalid RAM module."); continue; }
                Capacity(module.CapacityGB, "ram.capacityGB"); Text(module.Manufacturer, "ram.manufacturer");
                Text(module.PartNumber, "ram.partNumber");
                if (module.SpeedMHz is < 0 or > 1_000_000) errors.Add("Invalid RAM speed.");
            }
        }
        if (data.Disks is { } disks)
        {
            if (disks.Count > 256) errors.Add("Too many disks.");
            foreach (var disk in disks)
            {
                if (disk is null) { errors.Add("Invalid disk."); continue; }
                Text(disk.Model, "disk.model"); Text(disk.SerialNumber, "disk.serialNumber"); Text(disk.BusType, "disk.busType");
                Capacity(disk.CapacityGB, "disk.capacityGB");
                if (disk.MediaType is not (null or "SSD" or "HDD" or "NVMe" or "Unknown")) errors.Add("Invalid disk mediaType.");
            }
        }
        if (data.Windows is { } os)
        {
            Text(os.Edition, "windows.edition"); Text(os.Version, "windows.version");
            Text(os.BuildNumber, "windows.buildNumber"); Text(os.Architecture, "windows.architecture");
        }
        if (data.NetworkAdapters is { } adapters)
        {
            if (adapters.Count > 128) errors.Add("Too many network adapters.");
            foreach (var adapter in adapters)
            {
                if (adapter is null) { errors.Add("Invalid network adapter."); continue; }
                Text(adapter.Name, "adapter.name");
                if (adapter.MacAddress is not null && !MacPattern().IsMatch(adapter.MacAddress)) errors.Add("Invalid MAC address.");
            }
        }
        return errors;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex BranchPattern();
    [GeneratedRegex("^(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex MacPattern();
}

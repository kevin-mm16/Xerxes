using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using MiLife.DeviceCollector.Configuration;
using MiLife.DeviceCollector.Diagnostics;
using MiLife.DeviceContracts;

namespace MiLife.DeviceCollector.Hardware;

public sealed class WindowsHardwareCollector(CollectorSettings settings, LocalFiles files) : IHardwareCollector
{
    // Individual queries and properties are isolated so missing/denied WMI data remains optional.
    private List<Dictionary<string, object?>> Query(string name, string fields, string scope = @"root\cimv2")
    {
        var result = new List<Dictionary<string, object?>>();
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(8), ReturnImmediately = true };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery($"SELECT {fields} FROM {name}"), options);
            using var rows = searcher.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var field in fields.Split(','))
                    {
                        try { values[field.Trim()] = row[field.Trim()]; }
                        catch (Exception ex) { files.Log("HardwarePropertyUnavailable", name + "." + field.Trim() + ":" + ex.GetType().Name); }
                    }
                    result.Add(values);
                }
            }
        }
        catch (Exception ex) { files.Log("HardwareQueryUnavailable", name + ":" + ex.GetType().Name); }
        return result;
    }

    private static string? Text(Dictionary<string, object?>? row, string field)
    {
        if (row is null || !row.TryGetValue(field, out var value) || value is null) return null;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : new string(text.Where(c => !char.IsControl(c)).Take(256).ToArray());
    }
    private static double? Number(Dictionary<string, object?>? row, string field) =>
        double.TryParse(Text(row, field), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0 ? value : null;
    private static int? Integer(Dictionary<string, object?>? row, string field) =>
        int.TryParse(Text(row, field), out var value) && value >= 0 ? value : null;
    private static double? RamGB(double? bytes) => bytes is null ? null : Math.Round(bytes.Value / 1073741824d, 2);
    private static double? DiskGB(double? bytes) => bytes is null ? null : Math.Round(bytes.Value / 1_000_000_000d, 2);

    public DeviceInventory Collect(string branchCode)
    {
        var system = Query("Win32_ComputerSystem", "Manufacturer,Model,UserName,TotalPhysicalMemory,PCSystemType").FirstOrDefault();
        var chassis = Query("Win32_SystemEnclosure", "ChassisTypes")
            .SelectMany(row => row.GetValueOrDefault("ChassisTypes") is ushort[] values ? values : Array.Empty<ushort>()).ToArray();
        var bios = Query("Win32_BIOS", "SerialNumber").FirstOrDefault();
        var cpus = Query("Win32_Processor", "Manufacturer,Name,NumberOfCores,NumberOfLogicalProcessors");
        var modules = Query("Win32_PhysicalMemory", "Capacity,Manufacturer,PartNumber,Speed")
            .Select(row => new MemoryModuleInfo(RamGB(Number(row, "Capacity")), Text(row, "Manufacturer"), Text(row, "PartNumber"), Integer(row, "Speed"))).ToList();
        var os = Query("Win32_OperatingSystem", "Caption,Version,BuildNumber,OSArchitecture").FirstOrDefault();
        var physicalDisks = Query("MSFT_PhysicalDisk", "DeviceId,FriendlyName,SerialNumber,Size,MediaType,BusType", @"root\Microsoft\Windows\Storage");
        var diskDrives = Query("Win32_DiskDrive", "Index,Model,SerialNumber,Size,InterfaceType");
        var disks = new List<DiskInfo>();
        foreach (var row in diskDrives)
        {
            var serial = Text(row, "SerialNumber");
            var matches = physicalDisks.Where(d => !string.IsNullOrWhiteSpace(serial)
                && string.Equals(Text(d, "SerialNumber"), serial, StringComparison.OrdinalIgnoreCase)).ToList();
            // Fall back to disk index only when the provider's DeviceId is numeric and size also agrees.
            var extra = matches.Count == 1 ? matches[0] : physicalDisks.FirstOrDefault(d =>
                Integer(d, "DeviceId") is { } id && id == Integer(row, "Index")
                && Number(d, "Size") is { } size && size == Number(row, "Size"));
            disks.Add(new DiskInfo(Text(row, "Model") ?? Text(extra, "FriendlyName"), serial ?? Text(extra, "SerialNumber"),
                DiskGB(Number(row, "Size") ?? Number(extra, "Size")), Media(extra), Bus(extra) ?? Text(row, "InterfaceType")));
        }
        if (diskDrives.Count == 0)
            disks.AddRange(physicalDisks.Select(d => new DiskInfo(Text(d, "FriendlyName"), Text(d, "SerialNumber"), DiskGB(Number(d, "Size")), Media(d), Bus(d))));

        var adapters = new List<NetworkAdapterInfo>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var mac = adapter.GetPhysicalAddress().GetAddressBytes();
                    if (mac.Length != 6) continue;
                    adapters.Add(new NetworkAdapterInfo(adapter.Name, string.Join(":", mac.Select(b => b.ToString("X2")))));
                }
                catch (Exception ex) { files.Log("NetworkAdapterUnavailable", ex.GetType().Name); }
            }
        }
        catch (Exception ex) { files.Log("NetworkUnavailable", ex.GetType().Name); }

        int? SumCpu(string field)
        {
            var counts = cpus.Select(row => Integer(row, field)).ToList();
            return counts.Count > 0 && counts.All(c => c is > 0) ? counts.Sum(c => c!.Value) : null;
        }
        string? CpuNames(string field)
        {
            var names = string.Join(" / ", cpus.Select(row => Text(row, field)).Where(s => s is not null).Distinct());
            return names.Length == 0 ? null : names[..Math.Min(names.Length, 256)];
        }
        return new DeviceInventory
        {
            CollectionId = Guid.NewGuid(), CollectorVersion = settings.CollectorVersion, BranchCode = branchCode,
            ComputerName = Environment.MachineName, LoggedInUser = Text(system, "UserName") ?? Environment.UserName,
            Manufacturer = Text(system, "Manufacturer"), Model = Text(system, "Model"), SerialNumber = Text(bios, "SerialNumber"),
            DeviceType = DeviceClassification.Classify(chassis, Integer(system, "PCSystemType")),
            Processor = new ProcessorInfo(CpuNames("Manufacturer"), CpuNames("Name"), SumCpu("NumberOfCores"), SumCpu("NumberOfLogicalProcessors")),
            Ram = new MemoryInfo(modules.Count > 0 && modules.All(m => m.CapacityGB is not null)
                ? Math.Round(modules.Sum(m => m.CapacityGB!.Value), 2) : RamGB(Number(system, "TotalPhysicalMemory")), modules),
            Disks = disks, Windows = new WindowsInfo(Text(os, "Caption"), Text(os, "Version"), Text(os, "BuildNumber"), Text(os, "OSArchitecture")),
            NetworkAdapters = adapters, CollectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string Media(Dictionary<string, object?>? row) => Integer(row, "BusType") == 17 ? "NVMe" : Integer(row, "MediaType") switch
    { 3 => "HDD", 4 => "SSD", _ => "Unknown" };
    private static string? Bus(Dictionary<string, object?>? row) => Integer(row, "BusType") switch
    { 1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "IEEE 1394", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID", 9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 14 => "Virtual", 15 => "File-backed virtual", 16 => "Storage Spaces", 17 => "NVMe", 18 => "SCM", 19 => "UFS", _ => null };
}

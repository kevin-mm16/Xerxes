namespace MiLife.DeviceCollector.Hardware;

public static class DeviceClassification
{
    // SMBIOS values: https://learn.microsoft.com/windows/win32/cimwin32prov/win32-systemenclosure
    public static string Classify(IEnumerable<ushort> chassisTypes, int? systemType)
    {
        var types = chassisTypes.ToArray();
        var laptop = types.Any(t => t is 8 or 9 or 10 or 14 or 31 or 32);
        var desktop = types.Any(t => t is 3 or 4 or 5 or 6 or 7 or 13 or 15 or 16 or 35 or 36);
        if (laptop && desktop) return "Unknown";
        if (laptop) return "Laptop";
        if (desktop) return "Desktop";
        // Explicit other chassis (e.g. tablet/server) must not become a laptop/desktop.
        if (types.Any(t => t > 2)) return "Unknown";
        return systemType switch { 1 or 3 => "Desktop", 2 => "Laptop", _ => "Unknown" };
    }
}

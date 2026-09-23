namespace MiLife.DeviceIntake.Api.Services;

public sealed record SupportCommandTemplate(string Id, string Name, string Description, string Category, bool DefaultRunSilently, string Script);

public static class SupportCommandCatalog
{
    private static readonly IReadOnlyDictionary<string, SupportCommandTemplate> Commands = new[]
    {
        new SupportCommandTemplate("system-health", "System health", "Windows, uptime, memory and computer information.", "Health", true, """
            $os = Get-CimInstance Win32_OperatingSystem
            $computer = Get-CimInstance Win32_ComputerSystem
            [pscustomobject]@{
              ComputerName = $env:COMPUTERNAME
              Windows = $os.Caption
              Version = $os.Version
              LastBoot = $os.LastBootUpTime
              UptimeHours = [math]::Round(((Get-Date) - $os.LastBootUpTime).TotalHours, 1)
              TotalMemoryGB = [math]::Round($computer.TotalPhysicalMemory / 1GB, 1)
              FreeMemoryGB = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
            } | Format-List | Out-String -Width 220
            """),
        new SupportCommandTemplate("network-status", "Network status", "Active adapters, addresses, DNS and gateway configuration.", "Network", true, """
            Get-NetIPConfiguration | Where-Object NetAdapter.Status -eq 'Up' |
              Select-Object InterfaceAlias,InterfaceDescription,
                @{n='IPv4';e={$_.IPv4Address.IPAddress -join ', '}},
                @{n='Gateway';e={$_.IPv4DefaultGateway.NextHop -join ', '}},
                @{n='DNS';e={$_.DNSServer.ServerAddresses -join ', '}} |
              Format-List | Out-String -Width 220
            """),
        new SupportCommandTemplate("storage-health", "Storage health", "Local disk capacity and free-space summary.", "Health", true, """
            Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" |
              Select-Object DeviceID,VolumeName,
                @{n='SizeGB';e={[math]::Round($_.Size/1GB,1)}},
                @{n='FreeGB';e={[math]::Round($_.FreeSpace/1GB,1)}},
                @{n='FreePercent';e={if($_.Size){[math]::Round(100*$_.FreeSpace/$_.Size,1)}}} |
              Format-Table -AutoSize | Out-String -Width 220
            """),
        new SupportCommandTemplate("top-processes", "Top processes", "Twenty processes using the most CPU time and memory.", "Performance", true, """
            Get-Process -ErrorAction SilentlyContinue | Sort-Object CPU -Descending |
              Select-Object -First 20 Name,Id,
                @{n='CPUSeconds';e={[math]::Round($_.CPU,1)}},
                @{n='MemoryMB';e={[math]::Round($_.WorkingSet64/1MB,1)}} |
              Format-Table -AutoSize | Out-String -Width 220
            """),
        new SupportCommandTemplate("security-status", "Windows security", "Microsoft Defender health available to the signed-in user.", "Security", true, """
            $status = Get-MpComputerStatus -ErrorAction Stop
            $status | Select-Object AMServiceEnabled,AntivirusEnabled,AntispywareEnabled,
              RealTimeProtectionEnabled,BehaviorMonitorEnabled,IoavProtectionEnabled,
              AntivirusSignatureLastUpdated,QuickScanEndTime,FullScanEndTime |
              Format-List | Out-String -Width 220
            """),
        new SupportCommandTemplate("recent-system-errors", "Recent system errors", "Up to 25 critical and error events from the last 24 hours.", "Diagnostics", true, """
            Get-WinEvent -FilterHashtable @{LogName='System';Level=1,2;StartTime=(Get-Date).AddHours(-24)} -MaxEvents 25 -ErrorAction SilentlyContinue |
              Select-Object TimeCreated,Id,ProviderName,LevelDisplayName,Message |
              Format-List | Out-String -Width 220
            """)
    }.ToDictionary(command => command.Id, StringComparer.Ordinal);

    public static IEnumerable<SupportCommandTemplate> All => Commands.Values;
    public static bool TryGet(string? id, out SupportCommandTemplate template) => Commands.TryGetValue(id ?? "", out template!);
}

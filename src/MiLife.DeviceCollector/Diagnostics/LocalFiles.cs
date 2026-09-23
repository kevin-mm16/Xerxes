using System.Text.Json;
using MiLife.DeviceContracts;

namespace MiLife.DeviceCollector.Diagnostics;

public sealed class LocalFiles
{
    private readonly string root;
    private readonly string version;
    private readonly object gate = new();
    public LocalFiles(string version, string? root = null)
    {
        this.version = version;
        this.root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiLifeDeviceCollector");
    }

    // Log only controlled event names, status codes and exception types, never messages/bodies/headers.
    public void Log(string eventName, string? detail = null)
    {
        try
        {
            lock (gate)
            {
                var directory = Path.Combine(root, "Logs");
                Directory.CreateDirectory(directory);
                var current = Path.Combine(directory, "collector.log");
                if (File.Exists(current) && new FileInfo(current).Length > 256 * 1024)
                {
                    for (var index = 3; index >= 1; index--)
                    {
                        var source = index == 1 ? current : Path.Combine(directory, $"collector.{index - 1}.log");
                        if (File.Exists(source)) File.Move(source, Path.Combine(directory, $"collector.{index}.log"), true);
                    }
                }
                File.AppendAllText(current, JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, version, eventName, detail }) + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Logging must not stop collection. */ }
    }

    public async Task<string> SavePendingAsync(DeviceInventory data, CancellationToken ct = default)
    {
        var directory = Path.Combine(root, "Pending");
        Directory.CreateDirectory(directory);
        var serial = new string((data.SerialNumber ?? "UNKNOWN").Take(128)
            .Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
        if (string.IsNullOrWhiteSpace(serial)) serial = "UNKNOWN";
        var stem = $"{serial}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var path = Path.Combine(directory, stem + ".json");
        FileStream stream;
        try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
        catch (IOException) when (File.Exists(path))
        {
            path = Path.Combine(directory, $"{stem}-{Guid.NewGuid():N}.json");
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        await using (stream)
            await JsonSerializer.SerializeAsync(stream, data, InventoryJson.Options, ct);
        return path;
    }
}

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiLife.DeviceCollector;
using MiLife.DeviceCollector.Configuration;
using MiLife.DeviceCollector.Diagnostics;
using MiLife.DeviceCollector.Hardware;
using MiLife.DeviceCollector.Submission;

Console.WriteLine("MiLife Device Inventory Collector\n");
Console.WriteLine("Collects company hardware, Windows details, your Windows username and active adapter MAC addresses for IT asset records.\n");
var exitCode = 1;
LocalFiles? files = null;
try
{
    var branch = "UNKNOWN";
    if (args.Length != 0)
    {
        if (args.Length != 2 || args[0] != "--branch" || !Regex.IsMatch(args[1], "\\A[A-Za-z0-9_-]{1,32}\\z"))
            throw new ArgumentException("Usage: MiLifeDeviceCollector.exe [--branch BRANCHCODE]");
        branch = args[1].ToUpperInvariant();
    }
    var settings = CollectorSettings.Load();
    files = new LocalFiles(settings.CollectorVersion);
    var services = new ServiceCollection();
    services.AddLogging(builder => builder.ClearProviders());
    services.AddSingleton(settings);
    services.AddSingleton(files);
    services.AddSingleton<IRetryDelay, RetryDelay>();
    services.AddSingleton<IHardwareCollector, WindowsHardwareCollector>();
    services.AddTransient<CollectorApplication>();
    services.AddHttpClient<SubmissionClient>(http => { http.Timeout = TimeSpan.FromSeconds(30); http.MaxResponseContentBufferSize = 64 * 1024; })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    await using var provider = services.BuildServiceProvider();
    exitCode = await provider.GetRequiredService<CollectorApplication>().RunAsync(branch);
}
catch (ArgumentException) { Console.WriteLine("Usage: MiLifeDeviceCollector.exe [--branch BRANCHCODE]"); }
catch (Exception ex)
{
    files?.Log("CollectorFailed", ex.GetType().Name);
    Console.WriteLine("The collector could not finish. Please contact IT.");
}
if (!Console.IsInputRedirected)
{
    Console.WriteLine("\nPress any key to close.");
    try { Console.ReadKey(true); } catch (InvalidOperationException) { }
}
return exitCode;

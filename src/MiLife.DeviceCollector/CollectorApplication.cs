using MiLife.DeviceCollector.Diagnostics;
using MiLife.DeviceCollector.Hardware;
using MiLife.DeviceCollector.Submission;
using MiLife.DeviceContracts;

namespace MiLife.DeviceCollector;

public sealed class CollectorApplication(IHardwareCollector hardware, SubmissionClient submission, LocalFiles files)
{
    public async Task<int> RunAsync(string branchCode)
    {
        Console.WriteLine("Collecting computer information...");
        var inventory = hardware.Collect(branchCode);
        SubmissionResult result;
        if (!InventoryValidation.IsUsableSerial(inventory.SerialNumber))
            result = new(false, "A usable BIOS serial number could not be read. IT will need to identify this computer manually.");
        else
        {
            Console.WriteLine("Submitting information securely...");
            try { result = await submission.SubmitAsync(inventory); }
            catch (Exception ex) { files.Log("SubmissionFailed", ex.GetType().Name); result = new(false, "Unable to submit device information. Contact IT."); }
        }
        Console.WriteLine("\n" + result.Message);
        files.Log("SubmissionResult", result.Success ? "Success" : "Failure");
        if (result.Success)
        {
            Console.WriteLine($"\nComputer: {inventory.ComputerName ?? "Unknown"}\nMake: {inventory.Manufacturer ?? "Unknown"}\nModel: {inventory.Model ?? "Unknown"}\nSerial Number: {inventory.SerialNumber ?? "Unknown"}\nRAM: {inventory.Ram?.TotalGB?.ToString("0.##") ?? "Unknown"} GB");
            var storage = string.Join(", ", inventory.Disks?.Select(d => $"{d.CapacityGB?.ToString("0.##") ?? "Unknown"} GB {d.MediaType ?? "Unknown"}") ?? []);
            Console.WriteLine("Storage: " + (storage.Length == 0 ? "Unknown" : storage));
            Console.WriteLine("\nYou may close this window.");
            return 0;
        }
        try
        {
            var path = await files.SavePendingAsync(inventory);
            Console.WriteLine("\nThe collected information has been saved locally for IT:\n" + path);
            files.Log("RecoverySaved");
        }
        catch (Exception ex)
        {
            files.Log("RecoveryFailed", ex.GetType().Name);
            Console.WriteLine("\nA local copy could not be saved. Please contact IT and run the collector again when the problem is resolved.");
        }
        return 1;
    }
}

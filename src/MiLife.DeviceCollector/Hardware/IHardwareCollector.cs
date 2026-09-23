using MiLife.DeviceContracts;

namespace MiLife.DeviceCollector.Hardware;

public interface IHardwareCollector
{
    DeviceInventory Collect(string branchCode);
}

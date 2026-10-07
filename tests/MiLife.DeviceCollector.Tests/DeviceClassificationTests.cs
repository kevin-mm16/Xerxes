using MiLife.DeviceCollector.Hardware;

namespace MiLife.DeviceCollector.Tests;

public sealed class DeviceClassificationTests
{
    [Theory]
    [InlineData(9, 1, "Laptop")]
    [InlineData(10, 2, "Laptop")]
    [InlineData(31, 0, "Laptop")]
    [InlineData(3, 2, "Desktop")]
    [InlineData(13, 1, "Desktop")]
    [InlineData(30, 2, "Unknown")]
    [InlineData(23, 4, "Unknown")]
    [InlineData(2, 2, "Laptop")]
    [InlineData(2, 0, "Unknown")]
    public void UsesReportedChassisBeforeSystemFallback(int chassis, int system, string expected) =>
        Assert.Equal(expected, DeviceClassification.Classify([(ushort)chassis], system));

    [Fact]
    public void MissingOrConflictingDataIsNotInvented()
    {
        Assert.Equal("Unknown", DeviceClassification.Classify([], null));
        Assert.Equal("Unknown", DeviceClassification.Classify([3, 9], 2));
        Assert.Equal("Desktop", DeviceClassification.Classify([], 3));
    }
}

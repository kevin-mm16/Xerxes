using MiLife.DeviceContracts;
using MiLife.DeviceIntake.Api.Services;

namespace MiLife.DeviceIntake.Api.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData(" abc-123 ", "ABC-123")]
    [InlineData(" Ab C-12 ", "AB C-12")]
    [InlineData("\tA.B/12\r\n", "A.B/12")]
    public void SerialNormalizationPreservesIdentity(string input, string expected) => Assert.Equal(expected, InventoryValidation.NormalizeSerial(input));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("Unknown")] [InlineData("To be filled by O.E.M.")] [InlineData("00000000")]
    public void MissingAndPlaceholderSerialsAreRejected(string? serial) => Assert.False(InventoryValidation.IsUsableSerial(serial));

    [Fact]
    public void MalformedHardwareIsRejected()
    {
        var data = ApiTests.Payload() with
        {
            BranchCode = "../branch", Processor = new(null, null, -2, 0),
            Ram = new(double.NaN, []), NetworkAdapters = [new("adapter", "invalid")],
            CollectedAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        Assert.True(InventoryValidation.Validate(data, DateTimeOffset.UtcNow).Count >= 5);
    }

    [Fact]
    public void MissingOptionalHardwareIsAccepted() => Assert.Empty(InventoryValidation.Validate(ApiTests.Payload(), DateTimeOffset.UtcNow));

    [Fact]
    public void TokenRolesAreSeparateAndExact()
    {
        var validator = new TokenValidator(new string('i', 40), new string('a', 40));
        Assert.True(validator.Validate(new string('i', 40)));
        Assert.True(validator.Validate(new string('a', 40), admin: true));
        Assert.False(validator.Validate(new string('i', 40), admin: true));
        Assert.False(validator.Validate(new string('I', 40)));
        Assert.False(validator.Validate(null)); Assert.False(validator.Validate(""));
        Assert.Throws<InvalidOperationException>(() => new TokenValidator(new string('i', 40), new string('i', 40)));
        Assert.Throws<InvalidOperationException>(() => new TokenValidator("weak", new string('a', 40)));
    }
}

using System.Security.Cryptography;
using System.Text;

namespace MiLife.DeviceIntake.Api.Services;

public sealed class TokenValidator
{
    private readonly byte[] intakeHash;
    private readonly byte[] adminHash;
    public TokenValidator(string intakeToken, string adminToken)
    {
        if (!ValidConfiguration(intakeToken) || !ValidConfiguration(adminToken) || intakeToken == adminToken)
            throw new InvalidOperationException("Configure distinct DEVICE_INTAKE_TOKEN and DEVICE_ADMIN_TOKEN values of 32–512 printable ASCII characters.");
        intakeHash = Hash(intakeToken); adminHash = Hash(adminToken);
    }
    private static bool ValidConfiguration(string token) => token.Length is >= 32 and <= 512
        && token.All(c => c >= '!' && c <= '~') && !token.Contains("CHANGE_ME", StringComparison.Ordinal);
    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
    public bool Validate(string? candidate, bool admin = false) => candidate is { Length: > 0 and <= 512 }
        && CryptographicOperations.FixedTimeEquals(Hash(candidate), admin ? adminHash : intakeHash);
}

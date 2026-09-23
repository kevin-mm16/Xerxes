using System.Security.Cryptography;
using System.Text;

namespace MiLife.DeviceIntake.Api.Services;

public sealed class DashboardCredentials(IConfiguration configuration)
{
    public string Username => configuration["DEVICE_ADMIN_USERNAME"] ?? "root";
    private string EncodedHash => configuration["DEVICE_ADMIN_PASSWORD_HASH"] ?? "";
    public bool IsConfigured => TryParse(EncodedHash, out _, out _, out _);
    public string SessionVersion => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Username + ":" + EncodedHash)));

    public bool Validate(string? username, string? password)
    {
        if (password is null || password.Length > 1024 || !TryParse(EncodedHash, out var iterations, out var salt, out var hash)) return false;
        var candidate = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, hash.Length);
        var valid = CryptographicOperations.FixedTimeEquals(candidate, hash);
        return valid && string.Equals(username, Username, StringComparison.Ordinal);
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        return $"v1.600000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool TryParse(string encoded, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0; salt = []; hash = [];
        try
        {
            var parts = encoded.Split('.');
            if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out iterations) || iterations is < 100_000 or > 2_000_000) return false;
            salt = Convert.FromBase64String(parts[2]); hash = Convert.FromBase64String(parts[3]);
            return salt.Length == 16 && hash.Length == 32;
        }
        catch (FormatException) { return false; }
    }
}

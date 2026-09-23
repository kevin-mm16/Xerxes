using System.Reflection;
using System.Text.Json;

namespace MiLife.DeviceCollector.Configuration;

public sealed record CollectorSettings
{
    public string ApiUrl { get; init; } = "https://example.ngrok.app/api/device-intake";
    public string EnrollmentToken { get; init; } = "CHANGE_ME";
    public string CollectorVersion { get; init; } = "1.0.0";

    public static CollectorSettings Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CollectorSettings");
        return stream is null ? new() : JsonSerializer.Deserialize<CollectorSettings>(stream) ?? new();
    }

    public bool IsConfigured => Uri.TryCreate(ApiUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && EnrollmentToken.Length >= 32 && EnrollmentToken.Length <= 512
        && EnrollmentToken.All(c => c >= '!' && c <= '~') && !EnrollmentToken.Contains("CHANGE_ME", StringComparison.Ordinal);
}

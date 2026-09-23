using Microsoft.Extensions.Options;
using MiLife.DeviceIntake.Api.Configuration;

namespace MiLife.DeviceIntake.Api.Endpoints;

public static class DownloadEndpoints
{
    public static void MapDownloads(this WebApplication app)
    {
        IResult Download(IOptions<IntakeOptions> options, IWebHostEnvironment environment)
        {
            var path = Path.GetFullPath(options.Value.AgentPath, environment.ContentRootPath);
            return File.Exists(path) ? Results.File(path, "application/octet-stream", "MiLifeDeviceRegistration.exe", enableRangeProcessing: true)
                : Results.Problem("Registration is temporarily unavailable. Please contact IT.", statusCode: 503);
        }
        app.MapGet("/download/device-agent", Download);
        app.MapGet("/download/device-collector", Download);
        app.MapGet("/device-registration", () => Results.Content(Page, "text/html; charset=utf-8"));
        app.MapGet("/", () => Results.Redirect("/device-registration"));
    }
    private const string Page = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Register your PC | MiLife</title><style>
:root{font-family:"Segoe UI",system-ui,sans-serif;color:#182b49;background:#f3f6fb;color-scheme:light}*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px}main{max-width:480px;width:100%;padding:44px;background:#fff;border:1px solid #e0e6ef;border-radius:18px;box-shadow:0 20px 70px #172b4910}.brand{font-weight:750;letter-spacing:-1px;font-size:26px;margin-bottom:38px;color:#162b4d}h1{font-size:30px;letter-spacing:-.8px;margin:0 0 14px}p{line-height:1.6;color:#536279;font-size:15px}.button{display:block;text-align:center;background:#162b4d;color:#fff;text-decoration:none;font-weight:650;border-radius:8px;padding:15px 20px;margin:28px 0 16px}.button:hover{background:#234679}a:focus-visible{outline:3px solid #628cce;outline-offset:4px}.note{font-size:12px;color:#728098;margin-bottom:0}
</style></head><body><main><div class="brand"><img src="/device-admin/milife-logo.png" alt="miLife Insurance" width="210" height="78"></div><h1>Check in your PC with us</h1>
<p>Download, open, and enter your full name.</p>
<a class="button" href="/download/device-collector">Click to download</a>
<p class="note">Company Windows PCs. IT records hardware, availability and Windows-permitted location. Support commands require your approval.</p>
</main></body></html>
""";
}

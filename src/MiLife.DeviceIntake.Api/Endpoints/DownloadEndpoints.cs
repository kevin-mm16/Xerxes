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
        IResult DownloadPreview(IOptions<IntakeOptions> options, IWebHostEnvironment environment)
        {
            var path = Path.GetFullPath(options.Value.PreviewAgentPath, environment.ContentRootPath);
            return File.Exists(path) ? Results.File(path, "application/octet-stream", "MiLifeDeviceRegistrationPreview.exe", enableRangeProcessing: true)
                : Results.Problem("The preview registration build is not available.", statusCode: 503);
        }
        app.MapGet("/download/device-agent", Download);
        app.MapGet("/download/device-collector", Download);
        app.MapGet("/device-registration", () => Results.Content(Page, "text/html; charset=utf-8"));
        app.MapGet("/device-registration-preview", () => Results.Content(PreviewPage, "text/html; charset=utf-8"));
        app.MapGet("/download/device-agent-preview", DownloadPreview);
        app.MapGet("/", () => Results.Redirect("/device-registration"));
    }
    private const string Page = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Register your PC | MiLife</title><style>
:root{font-family:"Segoe UI",system-ui,sans-serif;color:#182b49;background:#f3f6fb;color-scheme:light}*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px}main{max-width:480px;width:100%;padding:44px;background:#fff;border:1px solid #e0e6ef;border-radius:18px;box-shadow:0 20px 70px #172b4910}.brand{font-weight:750;letter-spacing:-1px;font-size:26px;margin-bottom:38px;color:#162b4d}h1{font-size:30px;letter-spacing:-.8px;margin:0 0 14px}p{line-height:1.6;color:#536279;font-size:15px}.button{display:block;text-align:center;background:#162b4d;color:#fff;text-decoration:none;font-weight:650;border-radius:8px;padding:15px 20px;margin:28px 0 16px}.button:hover{background:#234679}a:focus-visible{outline:3px solid #628cce;outline-offset:4px}.note{font-size:12px;color:#728098;margin-bottom:0}
</style></head><body><main><div class="brand"><img src="/device-admin/milife-logo.png" alt="miLife Insurance" width="210" height="78"></div><h1>Check in your PC with us</h1>
<p>Download and open the app. Review the company-device management notice, accept it, and enter your full name to finish check-in.</p>
<a class="button" href="/download/device-collector">Click to download</a>
<p class="note">Company Windows PCs. Collection begins only after the management notice is accepted.</p>
</main></body></html>
""";

    private const string PreviewPage = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Preview PC check-in | MiLife</title><style>
:root{font-family:"Segoe UI",system-ui,sans-serif;color:#182b49;background:#f3f6fb;color-scheme:light}*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px}main{max-width:500px;width:100%;padding:44px;background:#fff;border:1px solid #e0e6ef;border-radius:18px;box-shadow:0 20px 70px #172b4910}.preview{display:inline-block;font-size:10px;font-weight:750;letter-spacing:1.2px;color:#0a5757;background:#c9f4f1;border-radius:999px;padding:6px 10px;margin-bottom:24px}.brand{margin-bottom:30px}h1{font-size:30px;letter-spacing:-.8px;margin:0 0 14px}p{line-height:1.6;color:#536279;font-size:15px}.button{display:block;text-align:center;background:#162b4d;color:#fff;text-decoration:none;font-weight:650;border-radius:8px;padding:15px 20px;margin:28px 0 16px}.button:hover{background:#234679}.note{font-size:12px;color:#728098;margin-bottom:0}
</style></head><body><main><span class="preview">PREVIEW</span><div class="brand"><img src="/device-admin/milife-logo.png" alt="MiLife Insurance" width="210" height="78"></div><h1>Check in your PC with us</h1>
<p>The preview first shows the company-device management notice. After you accept it, enter your full name to finish check-in.</p>
<a class="button" href="/download/device-agent-preview">Download preview</a>
<p class="note">Test release 1.4. The current production registration download is unchanged.</p>
</main></body></html>
""";
}

using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MiLife.DeviceIntake.Api.Configuration;
using MiLife.DeviceIntake.Api.Data;
using MiLife.DeviceIntake.Api.Endpoints;
using MiLife.DeviceIntake.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var intakeOptions = builder.Configuration.GetSection("Intake").Get<IntakeOptions>() ?? new();
if (intakeOptions.RequestLimitBytes is < 1024 or > 1048576 || intakeOptions.RequestsPerMinute is < 1 or > 10000
    || intakeOptions.GlobalRequestsPerMinute is < 1 or > 100000)
    throw new InvalidOperationException("Invalid intake limits.");
builder.Services.Configure<IntakeOptions>(builder.Configuration.GetSection("Intake"));
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = intakeOptions.RequestLimitBytes);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    return new TokenValidator(configuration["DEVICE_INTAKE_TOKEN"] ?? "", configuration["DEVICE_ADMIN_TOKEN"] ?? "");
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DashboardCredentials>();
builder.Services.AddDataProtection().SetApplicationName("MiLife.DeviceIntake.Admin")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["Admin:DataProtectionPath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, ".keys")));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "__Secure-MiLifeAdmin";
    options.Cookie.Path = "/device-admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(4);
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = async context =>
    {
        var credentials = context.HttpContext.RequestServices.GetRequiredService<DashboardCredentials>();
        if (!credentials.IsConfigured || context.Principal?.FindFirst("credential-version")?.Value != credentials.SessionVersion)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
        }
    };
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Secure-MiLifeCsrf";
    options.Cookie.Path = "/device-admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
if (databaseProvider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
{
    var mysqlVersion = Version.Parse(builder.Configuration["Database:MySqlVersion"] ?? "8.4.0");
    builder.Services.AddDbContext<MySqlIntakeDbContext>(options => options.UseMySql(
        builder.Configuration.GetConnectionString("DeviceIntake"), new MySqlServerVersion(mysqlVersion)));
    builder.Services.AddScoped<IntakeDbContext>(services => services.GetRequiredService<MySqlIntakeDbContext>());
}
else if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddDbContext<IntakeDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("DeviceIntake")));
else throw new InvalidOperationException("Database:Provider must be Sqlite or MySql.");
builder.Services.AddScoped<SubmissionService>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear(); options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback); options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("dashboard-login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
    { PermitLimit = 5, Window = TimeSpan.FromMinutes(5), QueueLimit = 0, AutoReplenishment = true }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetFixedWindowLimiter("global", _ => new()
    { PermitLimit = intakeOptions.GlobalRequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("intake", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
    { PermitLimit = intakeOptions.RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests. Try again later." }, ct);
    };
});
var app = builder.Build();
_ = app.Services.GetRequiredService<TokenValidator>();
// Forwarded headers are accepted only from a local tunnel/reverse proxy.
// Also reject them when a transport supplies no peer address (for example TestServer).
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } peer || !IPAddress.IsLoopback(peer))
    {
        context.Request.Headers.Remove("X-Forwarded-For");
        context.Request.Headers.Remove("X-Forwarded-Proto");
    }
    await next(context);
});
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = context.Request.Path.StartsWithSegments("/device-admin")
        || context.Request.Path.StartsWithSegments("/device-admin-preview")
        ? "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'; form-action 'self'"
        : "default-src 'none'; style-src 'unsafe-inline'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
    context.Response.Headers.CacheControl = "no-store";
    if (context.Request.IsHttps) context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
    var isLocal = context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
    var healthProbe = isLocal && context.Request.Path == "/health";
    if (!context.Request.IsHttps && !healthProbe
        && !(app.Environment.IsDevelopment() && intakeOptions.AllowInsecureLocalDevelopment && isLocal))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = "HTTPS is required." }); return;
    }
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (BadHttpRequestException ex)
    {
        if (!context.Response.HasStarted)
        { context.Response.StatusCode = ex.StatusCode; await context.Response.WriteAsJsonAsync(new { error = "Invalid request." }); }
    }
    catch (Exception ex)
    {
        app.Logger.LogError("Request failed: {ExceptionType}", ex.GetType().Name);
        if (!context.Response.HasStarted)
        { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "The server could not complete the request." }); }
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/device-admin/api"))
    {
        var publicSessionRoute = context.Request.Path == "/device-admin/api/session" || context.Request.Path == "/device-admin/api/login";
        if (!publicSessionRoute && context.User.Identity?.IsAuthenticated != true)
        { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "Please sign in." }); return; }
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Session verification failed. Refresh the page." }); return; }
        }
    }
    await next(context);
});
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    var admin = context.Request.Path.StartsWithSegments("/api/admin");
    if (admin || context.Request.Path.StartsWithSegments("/api/device-intake") || context.Request.Path == "/api/device-agent/enroll")
    {
        var header = admin ? "X-Admin-Token" : "X-Enrollment-Token";
        var values = context.Request.Headers[header];
        if (values.Count != 1 || !context.RequestServices.GetRequiredService<TokenValidator>().Validate(values[0], admin))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized." }); return;
        }
    }
    await next(context);
});
app.MapGet("/health", async (IntakeDbContext db, CancellationToken ct) =>
{
    try
    {
        _ = await db.DeviceSubmissions.AsNoTracking().AnyAsync(ct);
        return Results.Ok(new { status = "healthy" });
    }
    catch (Exception) { return Results.Json(new { status = "unhealthy" }, statusCode: 503); }
});
app.MapIntake(); app.MapAdmin(); app.MapDownloads(); app.MapDashboard(); app.MapAgents(); app.MapSupport(); app.MapAgentActions();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<IntakeDbContext>().Database.MigrateAsync();
await app.RunAsync();

public partial class Program;

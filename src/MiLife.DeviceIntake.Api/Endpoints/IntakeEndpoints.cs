using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiLife.DeviceContracts;
using MiLife.DeviceIntake.Api.Configuration;
using MiLife.DeviceIntake.Api.Services;

namespace MiLife.DeviceIntake.Api.Endpoints;

public static class IntakeEndpoints
{
    public static void MapIntake(this WebApplication app)
    {
        app.MapPost("/api/device-intake", ReceiveAsync).RequireRateLimiting("intake");
    }

    private static async Task<IResult> ReceiveAsync(HttpContext context, SubmissionService service,
        IOptions<IntakeOptions> options, TimeProvider clock, ILogger<SubmissionService> logger)
    {
        if (!context.Request.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        var limit = options.Value.RequestLimitBytes;
        if (context.Request.ContentLength > limit) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        try
        {
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) != 0)
            {
                if (body.Length + count > limit) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                await body.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted);
            }
            var bytes = body.ToArray();
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            if (HasDuplicateFields(document.RootElement)) return Results.BadRequest(new { error = "Duplicate JSON fields are not allowed." });
            var data = JsonSerializer.Deserialize<DeviceInventory>(bytes, InventoryJson.Options);
            if (data is null) return Results.BadRequest(new { error = "A device payload is required." });
            var errors = InventoryValidation.Validate(data, clock.GetUtcNow());
            if (errors.Count > 0) return Results.BadRequest(new { errors });
            var receipt = await service.ReceiveAsync(data, Encoding.UTF8.GetString(bytes), context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted);
            logger.LogInformation("Submission {SubmissionId} accepted; repeat: {IsRepeat}", receipt.SubmissionId, receipt.IsRepeat);
            return Results.Ok(receipt);
        }
        catch (JsonException) { return Results.BadRequest(new { error = "Invalid JSON payload." }); }
        catch (CollectionConflictException) { return Results.Conflict(new { error = "collectionId already belongs to a different payload." }); }
        catch (DbUpdateException)
        {
            logger.LogWarning("Submission storage was unavailable or conflicted.");
            context.Response.Headers.RetryAfter = "2";
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning("Submission database is unavailable.");
            context.Response.Headers.RetryAfter = "2";
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (MySqlConnector.MySqlException)
        {
            logger.LogWarning("Submission database is unavailable or busy.");
            context.Response.Headers.RetryAfter = "2";
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool HasDuplicateFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateFields(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) if (HasDuplicateFields(item)) return true;
        return false;
    }
}

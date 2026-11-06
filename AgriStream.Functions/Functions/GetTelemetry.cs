using System.Net;
using AgriStream.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AgriStream.Functions.Functions;

/// <summary>
/// FUNCTION 2 - HTTP TRIGGER (GET).
/// GET /api/telemetry/{fieldId}?take=30
/// Called by the MVC dashboard. A Table INPUT BINDING reads the field's partition; RowKeys are
/// inverted timestamps, so the first rows returned are the newest readings.
/// </summary>
public class GetTelemetry
{
    private readonly ILogger<GetTelemetry> _log;

    public GetTelemetry(ILogger<GetTelemetry> log) => _log = log;

    [Function(nameof(GetTelemetry))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "telemetry/{fieldId}")] HttpRequestData req,
        string fieldId,
        [TableInput("Telemetry", "{fieldId}", Take = 100, Connection = "AgriStorage")] IEnumerable<TelemetryEntity> rows)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var take = int.TryParse(query["take"], out var t) ? Math.Clamp(t, 1, 100) : 30;

        var result = rows
            .OrderBy(r => r.RowKey, StringComparer.Ordinal)
            .Take(take)
            .Select(r => new
            {
                fieldId = r.PartitionKey,
                deviceId = r.DeviceId,
                soilMoisture = r.SoilMoisture,
                temperature = r.Temperature,
                humidity = r.Humidity,
                ph = r.Ph,
                readingTime = r.ReadingTime
            })
            .ToList();

        _log.LogInformation("Returned {Count} telemetry rows for {FieldId}", result.Count, fieldId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }
}

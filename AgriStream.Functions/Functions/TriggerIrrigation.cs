using System.Net;
using System.Text.Json;
using AgriStream.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AgriStream.Functions.Functions;

/// <summary>
/// FUNCTION 3 - HTTP TRIGGER (POST).
/// POST /api/irrigation  { deviceId, fieldId, durationMinutes, reason, requestedBy }
/// Validates the request and places an "Irrigation Command" on the Azure Storage Queue through a
/// Queue OUTPUT BINDING, then returns 202 Accepted immediately (the work happens asynchronously).
/// </summary>
public class TriggerIrrigation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<TriggerIrrigation> _log;

    public TriggerIrrigation(ILogger<TriggerIrrigation> log) => _log = log;

    [Function(nameof(TriggerIrrigation))]
    public async Task<IrrigationOutput> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "irrigation")] HttpRequestData req)
    {
        IrrigationRequest? body = null;
        try
        {
            body = await JsonSerializer.DeserializeAsync<IrrigationRequest>(req.Body, JsonOptions);
        }
        catch (JsonException)
        {
            // handled below as a bad request
        }

        if (body is null || string.IsNullOrWhiteSpace(body.DeviceId) || string.IsNullOrWhiteSpace(body.FieldId)
            || body.DurationMinutes is < 1 or > 240)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "deviceId, fieldId and durationMinutes (1-240) are required." });
            return new IrrigationOutput { HttpResponse = bad, Command = null };
        }

        var command = new IrrigationCommand
        {
            DeviceId = body.DeviceId.Trim(),
            FieldId = body.FieldId.Trim(),
            DurationMinutes = body.DurationMinutes,
            Reason = string.IsNullOrWhiteSpace(body.Reason) ? "Not specified" : body.Reason.Trim(),
            RequestedBy = string.IsNullOrWhiteSpace(body.RequestedBy) ? "unknown" : body.RequestedBy.Trim()
        };

        _log.LogInformation("Queued irrigation command {CommandId} for {DeviceId} ({Minutes} min) by {By}",
            command.CommandId, command.DeviceId, command.DurationMinutes, command.RequestedBy);

        var accepted = req.CreateResponse(HttpStatusCode.Accepted);
        await accepted.WriteAsJsonAsync(new { status = "queued", commandId = command.CommandId });

        return new IrrigationOutput { HttpResponse = accepted, Command = command };
    }
}

/// <summary>Multiple outputs: the HTTP response AND the queue message.</summary>
public class IrrigationOutput
{
    [QueueOutput("irrigation-commands", Connection = "AgriStorage")]
    public IrrigationCommand? Command { get; set; }

    public HttpResponseData HttpResponse { get; set; } = default!;
}

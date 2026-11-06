using System.Diagnostics;
using AgriStream.Web.Data;
using AgriStream.Web.Models;
using AgriStream.Web.Services;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgriStream.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private const string TelemetryTable = "Telemetry";
    private const double DrySoilThreshold = 20.0;

    private readonly AppDbContext _db;
    private readonly FunctionApiClient _functions;
    private readonly TableServiceClient _tables;
    private readonly ILogger<DashboardController> _log;

    public DashboardController(AppDbContext db, FunctionApiClient functions, TableServiceClient tables,
        ILogger<DashboardController> log)
    {
        _db = db;
        _functions = functions;
        _tables = tables;
        _log = log;
    }

    /// <summary>Field dashboard. Telemetry comes from the HTTP GET function; devices come from Azure SQL.</summary>
    public async Task<IActionResult> Index(string fieldId = "field-01")
    {
        var vm = new DashboardViewModel
        {
            FieldId = fieldId,
            Fields = await _db.Devices.Select(d => d.FieldId).Distinct().OrderBy(f => f).ToListAsync(),
            Devices = await _db.Devices.Where(d => d.FieldId == fieldId).OrderBy(d => d.DeviceId).ToListAsync(),
            RecentIrrigation = await _db.IrrigationLog.Where(l => l.FieldId == fieldId)
                .OrderByDescending(l => l.ProcessedAt).Take(8).ToListAsync(),
            Message = TempData["Message"] as string,
            Error = TempData["Error"] as string
        };

        var sw = Stopwatch.StartNew();
        try
        {
            vm.Readings = await _functions.GetLatestTelemetryAsync(fieldId, 30);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Telemetry function call failed for {FieldId}", fieldId);
            vm.Error ??= "Telemetry service is unavailable right now: " + ex.Message;
        }
        vm.TelemetryFetchMs = sw.ElapsedMilliseconds;

        return View(vm);
    }

    /// <summary>"Trigger Irrigation" button -> HTTP POST function -> Azure Storage Queue.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TriggerIrrigation(string fieldId, string deviceId, int durationMinutes = 15)
    {
        durationMinutes = Math.Clamp(durationMinutes, 1, 240);
        try
        {
            await _functions.SendIrrigationCommandAsync(new IrrigationRequest
            {
                FieldId = fieldId,
                DeviceId = deviceId,
                DurationMinutes = durationMinutes,
                Reason = "Manual trigger from dashboard",
                RequestedBy = User.Identity?.Name ?? "unknown"
            });
            TempData["Message"] = $"Irrigation command for {deviceId} ({durationMinutes} min) queued. " +
                                  "The device status updates once the queue function processes it - refresh in a few seconds.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Irrigation POST failed for {DeviceId}", deviceId);
            TempData["Error"] = "Could not queue the irrigation command: " + ex.Message;
        }
        return RedirectToAction(nameof(Index), new { fieldId });
    }

    /// <summary>
    /// Simulates a burst of soil-sensor readings and writes them straight into Azure Table Storage.
    /// If a reading is below the dry-soil threshold, the app automatically calls the POST function
    /// (the "automated sensor detects a problem" path).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Agronomist")]
    public async Task<IActionResult> SimulateReadings(string fieldId, int count = 12)
    {
        count = Math.Clamp(count, 1, 100);
        var table = _tables.GetTableClient(TelemetryTable);
        await table.CreateIfNotExistsAsync();

        var rnd = Random.Shared;
        var now = DateTime.UtcNow;
        var batch = new List<TableTransactionAction>();
        double lowestMoisture = double.MaxValue;
        string? driestSensor = null;

        for (int i = 0; i < count; i++)
        {
            var sensorId = $"{fieldId}-sensor-{(i % 4) + 1:D2}";
            var time = now.AddSeconds(-i * 15);
            // ~1 in 6 readings comes back dry so the automatic irrigation path can be demonstrated
            var moisture = rnd.Next(6) == 0 ? 10 + rnd.NextDouble() * 9 : 22 + rnd.NextDouble() * 23;

            var entity = new TelemetryEntity
            {
                PartitionKey = fieldId,
                RowKey = TelemetryEntity.NewRowKey(time, sensorId),
                DeviceId = sensorId,
                SoilMoisture = Math.Round(moisture, 1),
                Temperature = Math.Round(18 + rnd.NextDouble() * 16, 1),
                Humidity = Math.Round(35 + rnd.NextDouble() * 45, 1),
                Ph = Math.Round(5.6 + rnd.NextDouble() * 1.6, 2),
                ReadingTime = time
            };
            batch.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity));

            if (entity.SoilMoisture < lowestMoisture)
            {
                lowestMoisture = entity.SoilMoisture;
                driestSensor = sensorId;
            }
        }

        // Entity-group transactions: one round trip per 100 rows in the same partition
        foreach (var chunk in batch.Chunk(100))
            await table.SubmitTransactionAsync(chunk);

        var message = $"{count} simulated sensor readings written to Table Storage for {fieldId}.";

        if (lowestMoisture < DrySoilThreshold)
        {
            var valve = await _db.Devices.Where(d => d.FieldId == fieldId).OrderBy(d => d.DeviceId).FirstOrDefaultAsync();
            if (valve is not null)
            {
                try
                {
                    await _functions.SendIrrigationCommandAsync(new IrrigationRequest
                    {
                        FieldId = fieldId,
                        DeviceId = valve.DeviceId,
                        DurationMinutes = 20,
                        Reason = $"AUTO: {driestSensor} soil moisture {lowestMoisture:F1}% below {DrySoilThreshold}% threshold",
                        RequestedBy = "sensor-automation"
                    });
                    message += $" Dry soil detected ({lowestMoisture:F1}%) - automatic irrigation queued for {valve.DeviceId}.";
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Automatic irrigation failed");
                    TempData["Error"] = "Dry soil detected but the automatic irrigation call failed: " + ex.Message;
                }
            }
        }

        TempData["Message"] = message;
        return RedirectToAction(nameof(Index), new { fieldId });
    }
}

using AgriStream.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AgriStream.Functions.Functions;

/// <summary>
/// FUNCTION 4 - QUEUE TRIGGER.
/// Listens to "irrigation-commands", processes each command and updates the device's operational
/// status in the master registry (dbo.Devices) in Azure SQL Database, plus a history row in
/// dbo.IrrigationLog. Idempotent: a message delivered twice (queues are at-least-once) is only
/// applied once because CommandId is unique in IrrigationLog. After 5 failed attempts the runtime
/// moves the message to "irrigation-commands-poison" so nothing is lost.
/// </summary>
public class ProcessIrrigationCommand
{
    private const string Sql = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.IrrigationLog WHERE CommandId = @CommandId)
BEGIN
    MERGE dbo.Devices AS target
    USING (SELECT @DeviceId AS DeviceId) AS source
        ON target.DeviceId = source.DeviceId
    WHEN MATCHED THEN
        UPDATE SET Status = 'Irrigating',
                   IrrigationUntil = @Until,
                   LastCommandAt = @Now,
                   LastUpdatedBy = @RequestedBy
    WHEN NOT MATCHED THEN
        INSERT (DeviceId, FieldId, DeviceName, Status, IrrigationUntil, LastCommandAt, LastUpdatedBy)
        VALUES (@DeviceId, @FieldId, @DeviceId, 'Irrigating', @Until, @Now, @RequestedBy);

    INSERT INTO dbo.IrrigationLog
        (CommandId, DeviceId, FieldId, DurationMinutes, Reason, RequestedBy, RequestedAt, ProcessedAt)
    VALUES
        (@CommandId, @DeviceId, @FieldId, @Duration, @Reason, @RequestedBy, @RequestedAt, @Now);

    SELECT 1;
END
ELSE
    SELECT 0;

COMMIT TRANSACTION;";

    private readonly ILogger<ProcessIrrigationCommand> _log;

    public ProcessIrrigationCommand(ILogger<ProcessIrrigationCommand> log) => _log = log;

    [Function(nameof(ProcessIrrigationCommand))]
    public async Task Run(
        [QueueTrigger("irrigation-commands", Connection = "AgriStorage")] IrrigationCommand command,
        FunctionContext context)
    {
        var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("SqlConnectionString app setting is missing.");

        var now = DateTime.UtcNow;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(context.CancellationToken);

        await using var cmd = new SqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("@CommandId", command.CommandId);
        cmd.Parameters.AddWithValue("@DeviceId", command.DeviceId);
        cmd.Parameters.AddWithValue("@FieldId", command.FieldId);
        cmd.Parameters.AddWithValue("@Duration", command.DurationMinutes);
        cmd.Parameters.AddWithValue("@Reason", Truncate(command.Reason, 200));
        cmd.Parameters.AddWithValue("@RequestedBy", Truncate(command.RequestedBy, 100));
        cmd.Parameters.AddWithValue("@RequestedAt", command.RequestedAt);
        cmd.Parameters.AddWithValue("@Until", now.AddMinutes(command.DurationMinutes));
        cmd.Parameters.AddWithValue("@Now", now);

        var applied = Convert.ToInt32(await cmd.ExecuteScalarAsync(context.CancellationToken)) == 1;

        if (applied)
            _log.LogInformation("Device {DeviceId} set to Irrigating for {Minutes} min (command {CommandId}, lag {LagMs} ms)",
                command.DeviceId, command.DurationMinutes, command.CommandId, (now - command.RequestedAt).TotalMilliseconds);
        else
            _log.LogWarning("Duplicate delivery of command {CommandId} ignored", command.CommandId);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

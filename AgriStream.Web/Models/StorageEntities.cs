using Azure;
using Azure.Data.Tables;

namespace AgriStream.Web.Models;

/// <summary>
/// Azure Table Storage row for one sensor reading.
/// PartitionKey = FieldId, RowKey = inverted ticks + sensor id, so the newest readings sort first.
/// </summary>
public class TelemetryEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string DeviceId { get; set; } = string.Empty;
    public double SoilMoisture { get; set; }
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public double Ph { get; set; }
    public DateTime ReadingTime { get; set; }

    public static string NewRowKey(DateTime utc, string deviceId) =>
        $"{DateTime.MaxValue.Ticks - utc.Ticks:D19}_{deviceId}";
}

/// <summary>Audit row written to Table Storage by the Blob-triggered function.</summary>
public class ImageAuditEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string BlobName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}

/// <summary>Shape returned by the HTTP GET function.</summary>
public class TelemetryReading
{
    public string FieldId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public double SoilMoisture { get; set; }
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public double Ph { get; set; }
    public DateTime ReadingTime { get; set; }
}

/// <summary>Payload sent to the HTTP POST function.</summary>
public class IrrigationRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
}

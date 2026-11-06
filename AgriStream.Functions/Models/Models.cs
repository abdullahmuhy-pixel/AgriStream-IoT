using Azure;
using Azure.Data.Tables;

namespace AgriStream.Functions.Models;

/// <summary>Telemetry row in the Table Storage table "Telemetry" (PartitionKey = FieldId).</summary>
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
}

/// <summary>Audit row the Blob-trigger function writes to the Table Storage table "ImageAudit".</summary>
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

/// <summary>Message placed on the "irrigation-commands" queue.</summary>
public class IrrigationCommand
{
    public Guid CommandId { get; set; } = Guid.NewGuid();
    public string DeviceId { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Body accepted by the HTTP POST function.</summary>
public class IrrigationRequest
{
    public string? DeviceId { get; set; }
    public string? FieldId { get; set; }
    public int DurationMinutes { get; set; }
    public string? Reason { get; set; }
    public string? RequestedBy { get; set; }
}

using AgriStream.Functions.Models;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AgriStream.Functions.Functions;

/// <summary>
/// FUNCTION 1 - BLOB TRIGGER.
/// Fires when a drone image lands in the "drone-images" container. Reads the blob's properties and
/// metadata, validates it, and writes a processing-status audit row to Table Storage through a
/// Table OUTPUT BINDING (the return value) - no TableClient code needed.
/// </summary>
public class BlobUploadAudit
{
    private static readonly string[] ImageTypes = { "image/jpeg", "image/png", "image/tiff", "image/webp" };
    private readonly ILogger<BlobUploadAudit> _log;

    public BlobUploadAudit(ILogger<BlobUploadAudit> log) => _log = log;

    [Function(nameof(BlobUploadAudit))]
    [TableOutput("ImageAudit", Connection = "AgriStorage")]
    public async Task<ImageAuditEntity> Run(
        [BlobTrigger("drone-images/{name}", Connection = "AgriStorage")] BlobClient blob,
        string name)
    {
        var props = (await blob.GetPropertiesAsync()).Value;
        props.Metadata.TryGetValue("fieldId", out var fieldId);
        props.Metadata.TryGetValue("uploadedBy", out var uploadedBy);

        // Blob names are "{fieldId}/{timestamp}-{guid}.ext"; fall back to the folder if metadata is missing
        fieldId ??= name.Contains('/') ? name[..name.IndexOf('/')] : "unassigned";

        var contentType = props.ContentType ?? string.Empty;
        var isImage = ImageTypes.Contains(contentType.ToLowerInvariant());
        var status = isImage && props.ContentLength > 0 ? "Validated" : "Rejected";
        var notes = status == "Validated"
            ? $"Image accepted for analysis queue ({props.ContentLength / 1024} KB)."
            : $"Rejected: content type '{contentType}' or empty file.";

        _log.LogInformation("Drone image {Blob} ({Bytes} bytes, {Type}) for {Field}: {Status}",
            name, props.ContentLength, contentType, fieldId, status);

        return new ImageAuditEntity
        {
            PartitionKey = fieldId,
            RowKey = $"{DateTime.MaxValue.Ticks - DateTime.UtcNow.Ticks:D19}_{Guid.NewGuid():N}",
            BlobName = name,
            ContentType = contentType,
            SizeBytes = props.ContentLength,
            UploadedBy = uploadedBy ?? "unknown",
            Status = status,
            Notes = notes,
            ProcessedAt = DateTime.UtcNow
        };
    }
}

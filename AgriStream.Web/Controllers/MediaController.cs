using System.Text;
using AgriStream.Web.Data;
using AgriStream.Web.Models;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgriStream.Web.Controllers;

[Authorize]
public class MediaController : Controller
{
    private const string ContainerName = "drone-images";
    private const string AuditTable = "ImageAudit";
    private const long MaxBytes = 25 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp" };

    private readonly BlobServiceClient _blobs;
    private readonly TableServiceClient _tables;
    private readonly AppDbContext _db;

    public MediaController(BlobServiceClient blobs, TableServiceClient tables, AppDbContext db)
    {
        _blobs = blobs;
        _tables = tables;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var vm = new MediaViewModel
        {
            Message = TempData["Message"] as string,
            Error = TempData["Error"] as string,
            Fields = await _db.Devices.Select(d => d.FieldId).Distinct().OrderBy(f => f).ToListAsync()
        };

        var container = _blobs.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync();
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.Metadata))
            vm.Blobs.Add(blob);
        vm.Blobs = vm.Blobs.OrderByDescending(b => b.Properties.CreatedOn).Take(30).ToList();

        var audit = _tables.GetTableClient(AuditTable);
        await audit.CreateIfNotExistsAsync();
        await foreach (var row in audit.QueryAsync<ImageAuditEntity>(maxPerPage: 200))
            vm.Audit.Add(row);
        vm.Audit = vm.Audit.OrderByDescending(a => a.ProcessedAt).Take(30).ToList();

        return View(vm);
    }

    /// <summary>Uploads a drone image to Blob Storage. The Blob-triggered function then writes the audit row.</summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, string fieldId)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose an image to upload.";
            return RedirectToAction(nameof(Index));
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext) || file.Length > MaxBytes)
        {
            TempData["Error"] = $"Only image files ({string.Join(", ", AllowedExtensions)}) up to 25 MB are accepted.";
            return RedirectToAction(nameof(Index));
        }

        var container = _blobs.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None);

        var blobName = $"{fieldId}/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{ext}";
        var blob = container.GetBlobClient(blobName);

        await using var stream = file.OpenReadStream();
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType },
            Metadata = new Dictionary<string, string>
            {
                ["uploadedBy"] = AsciiOnly(User.Identity?.Name ?? "unknown"),
                ["fieldId"] = AsciiOnly(fieldId),
                ["originalName"] = AsciiOnly(Path.GetFileName(file.FileName))
            }
        });

        TempData["Message"] = $"Uploaded {file.FileName} to Blob Storage as {blobName}. " +
                              "The Blob-trigger function will add an audit entry shortly - refresh to see it.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Streams a private blob to the browser (the container is not public).</summary>
    public async Task<IActionResult> Image(string name)
    {
        var blob = _blobs.GetBlobContainerClient(ContainerName).GetBlobClient(name);
        if (!await blob.ExistsAsync()) return NotFound();
        var download = await blob.DownloadStreamingAsync();
        return File(download.Value.Content, download.Value.Details.ContentType ?? "application/octet-stream");
    }

    // Blob metadata values must be ASCII
    private static string AsciiOnly(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
            sb.Append(c is >= ' ' and <= '~' ? c : '_');
        return sb.ToString();
    }
}

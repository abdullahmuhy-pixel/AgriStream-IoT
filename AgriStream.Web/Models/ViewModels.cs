using System.ComponentModel.DataAnnotations;
using Azure.Storage.Blobs.Models;

namespace AgriStream.Web.Models;

public class LoginViewModel
{
    [Required] public string Username { get; set; } = string.Empty;
    [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public class CreateUserViewModel
{
    [Required, StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, Display(Name = "Role")]
    public int RoleId { get; set; }

    public List<Role> Roles { get; set; } = new();
}

public class DashboardViewModel
{
    public string FieldId { get; set; } = "field-01";
    public List<string> Fields { get; set; } = new();
    public List<TelemetryReading> Readings { get; set; } = new();
    public List<Device> Devices { get; set; } = new();
    public List<IrrigationLog> RecentIrrigation { get; set; } = new();
    public string? Message { get; set; }
    public string? Error { get; set; }
    public long TelemetryFetchMs { get; set; }
}

public class MediaViewModel
{
    public List<BlobItem> Blobs { get; set; } = new();
    public List<ImageAuditEntity> Audit { get; set; } = new();
    public List<string> Fields { get; set; } = new();
    public string? Message { get; set; }
    public string? Error { get; set; }
}

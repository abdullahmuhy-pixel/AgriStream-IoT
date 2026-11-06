using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgriStream.Web.Models;

/// <summary>System role: Farmer, Agronomist or Admin.</summary>
[Table("Roles")]
public class Role
{
    [Key]
    public int RoleId { get; set; }

    [Required, MaxLength(50)]
    public string RoleName { get; set; } = string.Empty;

    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}

/// <summary>Login credentials. Passwords are stored as salted PBKDF2 hashes, never plain text.</summary>
[Table("Users")]
public class AppUser
{
    [Key]
    public int UserId { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Master registry of field devices (irrigation valves). Updated by the Queue-triggered function.</summary>
[Table("Devices")]
public class Device
{
    [Key, MaxLength(50)]
    public string DeviceId { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string FieldId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DeviceName { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Status { get; set; } = "Idle";

    public DateTime? IrrigationUntil { get; set; }
    public DateTime? LastCommandAt { get; set; }

    [MaxLength(100)]
    public string? LastUpdatedBy { get; set; }

    /// <summary>An irrigation run ends on its own once IrrigationUntil has passed.</summary>
    [NotMapped]
    public string EffectiveStatus =>
        Status == "Irrigating" && IrrigationUntil.HasValue && IrrigationUntil.Value < DateTime.UtcNow
            ? "Idle"
            : Status;
}

/// <summary>History of processed irrigation commands (written by the Queue-triggered function).</summary>
[Table("IrrigationLog")]
public class IrrigationLog
{
    [Key]
    public int LogId { get; set; }
    public Guid CommandId { get; set; }

    [MaxLength(50)] public string DeviceId { get; set; } = string.Empty;
    [MaxLength(50)] public string FieldId { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    [MaxLength(200)] public string? Reason { get; set; }
    [MaxLength(100)] public string? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime ProcessedAt { get; set; }
}

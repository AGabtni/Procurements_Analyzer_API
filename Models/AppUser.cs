using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProcurePortal.API.Models;

[Table("app_user")]
public class AppUser
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("email")]
    public string Email { get; set; } = null!;

    [Required]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = null!;

    [Required]
    [Column("full_name")]
    public string FullName { get; set; } = null!;

    [Required]
    [Column("role")]
    public string Role { get; set; } = "user";

    [Column("is_active")]
    public bool IsActive { get; set; } = false;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_login")]
    public DateTime? LastLogin { get; set; }

    [Column("email_confirmed")]
    public bool EmailConfirmed { get; set; } = false;

    [Column("email_confirmation_token")]
    public string? EmailConfirmationToken { get; set; }

    [Column("notifications_enabled")]
    public bool NotificationsEnabled { get; set; } = false;

    [Column("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    public CompanyProfile? CompanyProfile { get; set; }
}

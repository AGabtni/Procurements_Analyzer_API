using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProcurePortal.API.Models;

[Table("user_preferences")]
[PrimaryKey(nameof(UserId))]
public class UserPreferences
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("locale")]
    public string Locale { get; set; } = "en-CA";

    [Column("comms_locale")]
    public string CommsLocale { get; set; } = "en-CA";

    [Column("notifications_enabled")]
    public bool NotificationsEnabled { get; set; } = true;

    [Column("notify_expiry_enabled")]
    public bool NotifyExpiryEnabled { get; set; } = false;

    [Column("notify_hour")]
    public short NotifyHour { get; set; } = 8;

    [ForeignKey("UserId")]
    public AppUser User { get; set; } = null!;
}

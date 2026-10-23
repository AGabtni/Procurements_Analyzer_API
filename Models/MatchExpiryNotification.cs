using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProcurePortal.API.Models;

[Table("match_expiry_notifications")]
[PrimaryKey(nameof(UserId), nameof(DigestDate))]
public class MatchExpiryNotification
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("digest_date")]
    public DateOnly DigestDate { get; set; }

    [Column("sent_at")]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("UserId")]
    public AppUser User { get; set; } = null!;
}

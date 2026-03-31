using System.ComponentModel.DataAnnotations;

namespace GmailCleanup.Models;

public class ActionRecord
{
    [Key]
    public int Id { get; set; }

    public string SessionId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "trash", "untrash"
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset PerformedAt { get; set; } = DateTimeOffset.UtcNow;
}

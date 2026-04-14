using System.ComponentModel.DataAnnotations;

namespace MailMopper.Models;

public class EmailRecord
{
    [Key]
    public string MessageId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string FromDomain { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public long SizeEstimate { get; set; }

    public bool HasListUnsubscribe { get; set; }
    public string GmailLabels { get; set; } = string.Empty; // comma-separated
    public string GmailCategory { get; set; } = string.Empty; // CATEGORY_PROMOTIONS, etc.

    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
}

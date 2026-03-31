using System.ComponentModel.DataAnnotations;

namespace GmailCleanup.Models;

public class Classification
{
    [Key]
    public int Id { get; set; }

    public string MessageId { get; set; } = string.Empty;
    public ClassificationCategory Category { get; set; } = ClassificationCategory.Unclassified;
    public string Reason { get; set; } = string.Empty; // e.g., "rule:list-unsubscribe", "ai:marketing"
    public string ClassifiedBy { get; set; } = string.Empty; // "rule", "ai", "human"
    public double Confidence { get; set; } = 1.0;
    public ReviewDecision ReviewDecision { get; set; } = ReviewDecision.Pending;
    public DateTimeOffset ClassifiedAt { get; set; } = DateTimeOffset.UtcNow;

    public EmailRecord? Email { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace MailMopper.Models;

public class WhitelistEntry
{
    [Key]
    public int Id { get; set; }

    public string Pattern { get; set; } = string.Empty; // email or domain
    public string PatternType { get; set; } = "domain"; // "domain" or "email"
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

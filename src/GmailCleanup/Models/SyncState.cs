using System.ComponentModel.DataAnnotations;

namespace GmailCleanup.Models;

public class SyncState
{
    [Key]
    public string Key { get; set; } = "default";

    public string? LastHistoryId { get; set; }
    public int TotalMessagesFetched { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
}

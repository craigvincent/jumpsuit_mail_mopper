namespace GmailCleanup.Models;

public enum ReviewDecision
{
    Pending = 0,
    ApproveTrash = 1,
    Keep = 2,
    Whitelisted = 3,
    Executed = 4
}

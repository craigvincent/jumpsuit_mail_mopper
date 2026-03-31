namespace GmailCleanup.Services;

/// <summary>
/// Thin abstraction over the Gmail API operations used by the app.
/// Enables testing without a real Gmail connection.
/// </summary>
public interface IGmailApi
{
    /// <summary>
    /// Batch-modifies messages (add/remove labels).
    /// </summary>
    Task BatchModifyAsync(IList<string> messageIds, IList<string>? addLabelIds, IList<string>? removeLabelIds, CancellationToken ct);
}

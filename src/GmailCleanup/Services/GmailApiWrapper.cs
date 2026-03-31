using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;

namespace GmailCleanup.Services;

/// <summary>
/// Production wrapper that delegates to the real Google GmailService.
/// </summary>
public class GmailApiWrapper : IGmailApi
{
    private readonly GmailService _gmail;

    public GmailApiWrapper(GmailService gmail)
    {
        _gmail = gmail ?? throw new ArgumentNullException(nameof(gmail));
    }

    public async Task BatchModifyAsync(IList<string> messageIds, IList<string>? addLabelIds, IList<string>? removeLabelIds, CancellationToken ct)
    {
        var request = new BatchModifyMessagesRequest
        {
            Ids = messageIds,
            AddLabelIds = addLabelIds,
            RemoveLabelIds = removeLabelIds
        };

        await _gmail.Users.Messages.BatchModify(request, "me").ExecuteAsync(ct);
    }
}

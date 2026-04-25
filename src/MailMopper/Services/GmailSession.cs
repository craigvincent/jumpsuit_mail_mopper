using Google.Apis.Gmail.v1;

namespace MailMopper.Services;

public class GmailSession
{
    public GmailService? Service { get; set; }

    public bool IsAuthenticated => Service != null;
}

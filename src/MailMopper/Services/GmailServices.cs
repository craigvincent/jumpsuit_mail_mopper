namespace MailMopper.Services;

/// <summary>
/// Groups Gmail-related services to keep constructor parameter counts manageable.
/// </summary>
public record GmailServices(GmailAuthService Auth, GmailFetchService Fetch);

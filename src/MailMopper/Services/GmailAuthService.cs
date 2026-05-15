using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using MailMopper.Config;

namespace MailMopper.Services;

public class GmailAuthService
{
    private readonly AppSettings _settings;
    private readonly GmailSession _session;

    public GmailAuthService(AppSettings settings, GmailSession session)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async Task<GmailService> AuthenticateAsync(CancellationToken ct, Action<string>? onAuthUrl = null)
    {
        if (!File.Exists(_settings.Gmail.CredentialsPath))
        {
            throw new FileNotFoundException(
                $"The credentials.json file was not found at '{_settings.Gmail.CredentialsPath}'. " +
                "Please download it from Google Cloud Console and place it in the specified location.");
        }

        UserCredential credential;

        using (var stream = new FileStream(_settings.Gmail.CredentialsPath, FileMode.Open, FileAccess.Read))
        {
            var tokenStore = new FileDataStore(_settings.Gmail.TokenPath, fullPath: true);
            var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets;

            var authCallbackPort = _settings.Gmail.AuthCallbackPort;
            if (authCallbackPort > 0)
            {
                var receiver = new LoopbackCodeReceiver(authCallbackPort, onAuthUrl);
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secrets,
                    _settings.Gmail.Scopes,
                    "user",
                    ct,
                    tokenStore,
                    receiver);
            }
            else
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secrets,
                    _settings.Gmail.Scopes,
                    "user",
                    ct,
                    tokenStore);
            }
        }

        var service = new GmailService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "MailMopper"
        });

        _session.Service = service;
        return service;
    }
}

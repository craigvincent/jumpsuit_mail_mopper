using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using GmailCleanup.Config;

namespace GmailCleanup.Services
{
    public class GmailAuthService
    {
        private readonly AppSettings _settings;

        public GmailAuthService(AppSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<GmailService> AuthenticateAsync(CancellationToken ct)
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
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    _settings.Gmail.Scopes,
                    "user",
                    ct,
                    tokenStore);
            }

            var service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "GmailCleanup"
            });

            return service;
        }
    }
}

using MailMopper.Config;
using Microsoft.Extensions.Configuration;

namespace MailMopper.Commands;

/// <summary>
/// Shared helper methods for command initialization and configuration.
/// </summary>
internal static class CommandHelper
{
    /// <summary>
    /// Loads application settings from appsettings.json and environment variables.
    /// </summary>
    /// <returns>Configured AppSettings instance</returns>
    public static AppSettings LoadSettings()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddEnvironmentVariables("MAIL_MOPPER_")
            .Build();

        var settings = new AppSettings();
        config.Bind(settings);

        // Resolve relative paths against CWD so credentials.json can live in the project root
        if (!Path.IsPathRooted(settings.Gmail.CredentialsPath))
            settings.Gmail.CredentialsPath = Path.GetFullPath(settings.Gmail.CredentialsPath, Directory.GetCurrentDirectory());
        if (!Path.IsPathRooted(settings.Gmail.TokenPath))
            settings.Gmail.TokenPath = Path.GetFullPath(settings.Gmail.TokenPath, Directory.GetCurrentDirectory());
        if (!Path.IsPathRooted(settings.Classification.RulesPath))
            settings.Classification.RulesPath = Path.GetFullPath(settings.Classification.RulesPath, Directory.GetCurrentDirectory());

        return settings;
    }

}

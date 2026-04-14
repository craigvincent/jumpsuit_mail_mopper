using MailMopper.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MailMopper.Commands;

public class AuthCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var appSettings = CommandHelper.LoadSettings();

        AnsiConsole.MarkupLine("[bold blue]Gmail Authentication Setup[/]");
        AnsiConsole.MarkupLine($"Looking for credentials at: [yellow]{appSettings.Gmail.CredentialsPath}[/]");

        if (!File.Exists(appSettings.Gmail.CredentialsPath))
        {
            AnsiConsole.MarkupLine("[red]credentials.json not found![/]");
            AnsiConsole.MarkupLine("Please download OAuth2 credentials from Google Cloud Console:");
            AnsiConsole.MarkupLine("  1. Go to https://console.cloud.google.com/apis/credentials");
            AnsiConsole.MarkupLine("  2. Create OAuth 2.0 Client ID (Desktop application)");
            AnsiConsole.MarkupLine("  3. Download JSON and save as credentials.json in the app directory");
            return 1;
        }

        var authService = new GmailAuthService(appSettings);

        await AnsiConsole.Status()
            .StartAsync("Authenticating with Gmail...", async ctx =>
            {
                var gmail = await authService.AuthenticateAsync(CancellationToken.None);
                var profile = await gmail.Users.GetProfile("me").ExecuteAsync();
                AnsiConsole.MarkupLine($"[green]✓[/] Authenticated as: [bold]{profile.EmailAddress}[/]");
                AnsiConsole.MarkupLine($"[green]✓[/] Total messages: [bold]{profile.MessagesTotal}[/]");
            });

        return 0;
    }
}

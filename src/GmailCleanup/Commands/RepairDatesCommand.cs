using Spectre.Console;
using Spectre.Console.Cli;
using GmailCleanup.Services;

namespace GmailCleanup.Commands;

public class RepairDatesCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            AnsiConsole.MarkupLine("[bold blue]Repair Email Dates[/]");
            AnsiConsole.MarkupLine("[dim]Fixes emails whose date was incorrectly set to fetch time.[/]");
            AnsiConsole.WriteLine();

            var appSettings = CommandHelper.LoadSettings();
            var authService = new GmailAuthService(appSettings);
            using var dbContext = CommandHelper.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            var gmail = await AnsiConsole.Status()
                .StartAsync("Authenticating with Gmail...", async ctx =>
                {
                    return await authService.AuthenticateAsync(CancellationToken.None);
                });

            var fetchService = new GmailFetchService(gmail, dbContext, appSettings);

            var repaired = await fetchService.RepairDatesAsync(
                onStatus: msg => AnsiConsole.MarkupLine($"  {Markup.Escape(msg)}"),
                CancellationToken.None);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✓ Repaired {repaired} email date(s).[/]");

            if (repaired > 0)
                AnsiConsole.MarkupLine("[dim]Year filters in review will now show correct dates.[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }
}

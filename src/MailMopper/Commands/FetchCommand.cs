using System.ComponentModel;
using MailMopper.Services;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MailMopper.Commands;

public class FetchSettings : CommandSettings
{
    [CommandOption("--full")]
    [Description("Force full fetch instead of incremental")]
    public bool FullFetch { get; set; }
}

public class FetchCommand : AsyncCommand<FetchSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, FetchSettings settings)
    {
        try
        {
            AnsiConsole.MarkupLine("[bold blue]Gmail Email Fetch[/]");

            var appSettings = CommandHelper.LoadSettings();
            var authService = new GmailAuthService(appSettings);
            using var dbContext = CommandHelper.CreateDbContext();

            // Authenticate
            var gmail = await AnsiConsole.Status()
                .StartAsync("Authenticating with Gmail...", async ctx =>
                {
                    return await authService.AuthenticateAsync(CancellationToken.None);
                });

            // Ensure database is created
            await dbContext.Database.EnsureCreatedAsync();
            var fetchService = new GmailFetchService(gmail, dbContext, appSettings);

            // Determine fetch strategy
            bool isFullFetch = settings.FullFetch;
            if (!isFullFetch)
            {
                var lastSync = await dbContext.SyncStates
                    .FirstOrDefaultAsync(s => s.Key == "default", CancellationToken.None);

                isFullFetch = lastSync?.LastSyncAt == null;
            }

            AnsiConsole.MarkupLine(isFullFetch
                ? "[yellow]Performing full fetch...[/]"
                : "[yellow]Performing incremental fetch...[/]");

            var startTime = DateTime.UtcNow;

            // Fetch with progress
            int totalFetched = 0;
            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[bold green]Fetching emails[/]");
                    var progress = new Progress<(int fetched, int total)>(p =>
                    {
                        if (p.total > 0)
                            task.Value = (double)p.fetched / p.total * 100;
                    });
                    totalFetched = isFullFetch
                        ? await fetchService.FetchAllAsync(progress, CancellationToken.None)
                        : await fetchService.FetchIncrementalAsync(progress, CancellationToken.None);
                });

            var duration = DateTime.UtcNow - startTime;

            AnsiConsole.MarkupLine("[green]✓ Fetch complete![/]");
            AnsiConsole.MarkupLine($"  [bold]Total fetched:[/] {totalFetched}");
            AnsiConsole.MarkupLine($"  [bold]Time taken:[/] {duration.TotalSeconds:F1}s");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error during fetch: {ex.Message}[/]");
            return 1;
        }
    }
}

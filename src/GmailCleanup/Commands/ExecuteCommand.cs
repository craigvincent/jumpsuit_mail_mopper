using Spectre.Console;
using Spectre.Console.Cli;
using GmailCleanup.Data;
using GmailCleanup.Services;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;

namespace GmailCleanup.Commands;

public class ExecuteSettings : CommandSettings
{
    [CommandOption("--dry-run")]
    [Description("Preview what would be trashed without actually trashing")]
    public bool DryRun { get; set; }
    
    [CommandOption("--force")]
    [Description("Skip confirmation prompt")]
    public bool Force { get; set; }
}

public class ExecuteCommand : AsyncCommand<ExecuteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ExecuteSettings settings)
    {
        try
        {
            AnsiConsole.MarkupLine("[bold blue]Execute Actions[/]");
            
            var appSettings = CommandHelper.LoadSettings();
            var authService = new GmailAuthService(appSettings);
            using var dbContext = CommandHelper.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            // Count approved-for-trash classifications
            var approvedCount = await dbContext.Classifications
                .Where(c => c.ReviewDecision == Models.ReviewDecision.ApproveTrash)
                .CountAsync(CancellationToken.None);

            if (approvedCount == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No emails approved for trash.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"\n[bold]Preview:[/] {approvedCount} email(s) approved for trash");

            // If dry-run, stop here
            if (settings.DryRun)
            {
                AnsiConsole.MarkupLine("\n[yellow]Dry-run mode: no emails were trashed.[/]");
                return 0;
            }

            // Confirm if not force
            if (!settings.Force)
            {
                var confirm = AnsiConsole.Confirm("\n[bold yellow]Proceed with trashing these emails?[/]", false);
                if (!confirm)
                {
                    AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                    return 0;
                }
            }

            // Authenticate
            var gmail = await AnsiConsole.Status()
                .StartAsync("Authenticating with Gmail...", async ctx =>
                {
                    return await authService.AuthenticateAsync(CancellationToken.None);
                });

            var actionService = new ActionService(new GmailApiWrapper(gmail), dbContext, appSettings);

            // Execute with progress
            ActionSummary? result = null;

            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[bold green]Trashing emails[/]");
                    var progress = new Progress<(int processed, int total)>(p =>
                    {
                        if (p.total > 0) task.Value = (double)p.processed / p.total * 100;
                    });
                    result = await actionService.TrashApprovedAsync(settings.DryRun, progress, CancellationToken.None);
                });

            AnsiConsole.MarkupLine("[green]✓ Execution complete![/]");
            AnsiConsole.MarkupLine($"  [bold]Emails trashed:[/] {result?.EmailsTrashed}");
            AnsiConsole.MarkupLine($"  [bold]Errors:[/] {result?.Errors}");
            AnsiConsole.MarkupLine($"  [bold]Session ID:[/] {result?.SessionId}");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error during execution: {ex.Message}[/]");
            return 1;
        }
    }
}

using System.ComponentModel;
using System.Globalization;
using MailMopper.Services;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MailMopper.Commands;

public class UndoSettings : CommandSettings
{
    [CommandArgument(0, "[session-id]")]
    [Description("Session ID to undo (leave empty to list sessions)")]
    public string? SessionId { get; set; }
}

public class UndoCommand : AsyncCommand<UndoSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UndoSettings settings)
    {
        try
        {
            AnsiConsole.MarkupLine("[bold blue]Undo Actions[/]");

            using var dbContext = CommandHelper.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            var databaseService = new DatabaseService(dbContext);

            // If no session ID, list available sessions
            if (string.IsNullOrWhiteSpace(settings.SessionId))
            {
                var sessions = await databaseService.GetSessionsAsync(CancellationToken.None);

                if (!sessions.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]No previous sessions found.[/]");
                    return 0;
                }

                AnsiConsole.MarkupLine("[bold]Available sessions:[/]");
                var table = new Table();
                table.AddColumn("[bold]Session ID[/]");
                table.AddColumn("[bold]Date[/]");
                table.AddColumn("[bold]Action[/]");
                table.AddColumn("[bold]Count[/]", col => col.RightAligned());

                foreach (var session in sessions)
                {
                    table.AddRow(
                        session.SessionId,
                        session.PerformedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        session.Action,
                        session.Count.ToString(CultureInfo.InvariantCulture));
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine("\n[yellow]Use: undo <session-id> to undo a specific session[/]");
                return 0;
            }

            // Undo specific session
            var appSettings = CommandHelper.LoadSettings();
            var authService = new GmailAuthService(appSettings);

            var gmail = await AnsiConsole.Status()
                .StartAsync("Authenticating with Gmail...", async ctx =>
                {
                    return await authService.AuthenticateAsync(CancellationToken.None);
                });

            var actionService = new ActionService(new GmailApiWrapper(gmail), dbContext, appSettings);

            // Get count of actions for this session
            var actionCount = await dbContext.Actions
                .Where(a => a.SessionId == settings.SessionId && a.Action == "trash")
                .CountAsync(CancellationToken.None);

            if (actionCount == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No trash actions found for session: {settings.SessionId}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[bold]Session {settings.SessionId}:[/] {actionCount} action(s) to undo");

            // Confirm
            var confirm = AnsiConsole.Confirm("[bold yellow]Proceed with undoing?[/]", false);
            if (!confirm)
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                return 0;
            }

            // Undo with progress
            int restored = 0;

            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[bold green]Restoring emails from trash[/]");
                    var progress = new Progress<(int processed, int total)>(p =>
                    {
                        if (p.total > 0)
                            task.Value = (double)p.processed / p.total * 100;
                    });
                    restored = await actionService.UndoSessionAsync(settings.SessionId, progress, CancellationToken.None);
                });

            AnsiConsole.MarkupLine("[green]✓ Undo complete![/]");
            AnsiConsole.MarkupLine($"  [bold]Emails restored:[/] {restored}");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error during undo: {ex.Message}[/]");
            return 1;
        }
    }
}

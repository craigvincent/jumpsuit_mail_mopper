using MailMopper.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MailMopper.Commands;

public class ReviewCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            AnsiConsole.MarkupLine("[bold blue]Email Review[/]");

            var dbContext = CommandHelper.CreateDbContext();
            await using var _ = dbContext;
            await dbContext.Database.EnsureCreatedAsync();

            // Create and run review TUI
            var reviewApp = new ReviewApp(dbContext);
            await reviewApp.RunAsync(CancellationToken.None);

            AnsiConsole.MarkupLine("[green]✓ Review complete![/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error during review: {ex.Message}[/]");
            return 1;
        }
    }
}

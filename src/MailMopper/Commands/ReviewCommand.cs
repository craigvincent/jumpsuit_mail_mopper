using MailMopper.Data;
using MailMopper.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MailMopper.Commands;

public class ReviewCommand : AsyncCommand
{
    private readonly ReviewApp _reviewApp;
    private readonly AppDbContext _dbContext;

    public ReviewCommand(ReviewApp reviewApp, AppDbContext dbContext)
    {
        _reviewApp = reviewApp ?? throw new ArgumentNullException(nameof(reviewApp));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            AnsiConsole.MarkupLine("[bold blue]Email Review[/]");

            await _dbContext.Database.EnsureCreatedAsync();

            await _reviewApp.RunAsync(CancellationToken.None);

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

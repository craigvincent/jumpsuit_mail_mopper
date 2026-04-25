using MailMopper.Config;
using MailMopper.Data;
using MailMopper.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MailMopper.Commands;

public class RepairDatesCommand : AsyncCommand
{
    private readonly GmailAuthService _authService;
    private readonly AppDbContext _dbContext;
    private readonly AppSettings _appSettings;

    public RepairDatesCommand(GmailAuthService authService, AppDbContext dbContext, AppSettings appSettings)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            AnsiConsole.MarkupLine("[bold blue]Repair Email Dates[/]");
            AnsiConsole.MarkupLine("[dim]Fixes emails whose date was incorrectly set to fetch time.[/]");
            AnsiConsole.WriteLine();

            await _dbContext.Database.EnsureCreatedAsync();

            var gmail = await AnsiConsole.Status()
                .StartAsync("Authenticating with Gmail...", async ctx =>
                {
                    return await _authService.AuthenticateAsync(CancellationToken.None);
                });

            var fetchService = new GmailFetchService(gmail, _dbContext, _appSettings);

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

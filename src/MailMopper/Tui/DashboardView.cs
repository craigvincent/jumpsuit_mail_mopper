using System.Globalization;
using MailMopper.Models;
using Spectre.Console;

namespace MailMopper.Tui;

/// <summary>
/// Dashboard view — top-level overview of categories, year filter, statistics.
/// </summary>
public partial class ReviewApp
{
    private async Task<bool> ShowDashboardAsync(CancellationToken ct)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold blue]Gmail Cleanup — Review Dashboard[/]").LeftJustified());

        var yearLabel = _yearFilter.HasValue ? $"[bold cyan]{_yearFilter.Value}[/]" : "[dim]All years[/]";
        AnsiConsole.MarkupLine($"  Year filter: {yearLabel}");
        AnsiConsole.WriteLine();

        RenderYearBreakdown();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]#[/]").RightAligned())
            .AddColumn("[bold]Category[/]")
            .AddColumn(new TableColumn("[bold]Emails[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Senders[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Size[/]").RightAligned())
            .AddColumn("[bold]Top Domain[/]")
            .AddColumn("[bold]Progress[/]");

        for (int i = 0; i < _groups.Count; i++)
        {
            var g = _groups[i];
            var count = g.Classifications.Count;
            var senderCount = g.Classifications.Select(c => c.Email?.From).Distinct().Count();
            var size = FormatSize(g.Classifications.Sum(c => c.Email?.SizeEstimate ?? 0));
            var topDomain = g.Classifications
                .GroupBy(c => c.Email?.FromDomain ?? "unknown")
                .OrderByDescending(sg => sg.Count())
                .FirstOrDefault()?.Key ?? "-";

            var decided = g.Classifications.Count(c => c.ReviewDecision != ReviewDecision.Pending);
            var progressText = decided == count
                ? g.Decision switch
                {
                    ReviewDecision.ApproveTrash => "[red]✗ Trash[/]",
                    ReviewDecision.Keep => "[green]✓ Keep[/]",
                    ReviewDecision.Whitelisted => "[cyan]✓ Whitelisted[/]",
                    _ => "[green]✓ Done[/]"
                }
                : decided > 0
                    ? $"[yellow]{decided}/{count} reviewed[/]"
                    : "[dim]Not started[/]";

            table.AddRow(
                $"[bold]{i + 1}[/]",
                $"[bold]{g.Category}[/]",
                count.ToString("N0", CultureInfo.InvariantCulture),
                senderCount.ToString("N0", CultureInfo.InvariantCulture),
                size,
                Markup.Escape(topDomain.Length > 25 ? topDomain[..22] + "..." : topDomain),
                progressText);
        }

        AnsiConsole.Write(table);

        var totalRemaining = _groups.Sum(g => g.Classifications.Count);
        var totalRemainingSize = _groups.Sum(g => g.Classifications.Sum(c => c.Email?.SizeEstimate ?? 0));
        var totalPendingEmails = _groups.Sum(g => g.Classifications.Count(c => c.ReviewDecision == ReviewDecision.Pending));
        var totalTrashEmails = _groups.Sum(g => g.Classifications.Count(c => c.ReviewDecision == ReviewDecision.ApproveTrash));
        var totalKeepEmails = _groups.Sum(g => g.Classifications.Count(c => c.ReviewDecision == ReviewDecision.Keep));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]Total:[/] {totalRemaining:N0} emails ({FormatSize(totalRemainingSize)})");
        if (_previouslyTrashedCount > 0)
            AnsiConsole.MarkupLine($"  [dim]Already executed:[/] {_previouslyTrashedCount:N0} emails ({FormatSize(_previouslyTrashedSize)})");
        AnsiConsole.MarkupLine($"  [yellow]Pending:[/] {totalPendingEmails:N0}  [red]Trash:[/] {totalTrashEmails:N0}  [green]Keep:[/] {totalKeepEmails:N0}");
        if (_dirty)
            AnsiConsole.MarkupLine($"  [dim italic]Unsaved changes ({_unsavedActions} actions)[/]");
        AnsiConsole.WriteLine();

        var navHints = new List<string>
        {
            "[blue]#[/]=open category",
            "[cyan]Y[/]=year filter",
            "[bold]S[/]=save & exit",
            "[dim]Q[/]=quit"
        };
        AnsiConsole.MarkupLine($"  {string.Join("  │  ", navHints)}");

        var input = ReadCommand("[blue]Dashboard: [/]", "SQY");

        if (string.IsNullOrWhiteSpace(input))
            return true;

        if (input.Equals("S", StringComparison.OrdinalIgnoreCase))
        {
            if (_dirty)
            {
                await SaveAsync(ct);
                AnsiConsole.MarkupLine("[green]✓ Saved.[/]");
            }
            return false;
        }
        if (input.Equals("Q", StringComparison.OrdinalIgnoreCase))
        {
            if (_dirty)
            {
                if (AnsiConsole.Confirm("[yellow]You have unsaved changes. Save before quitting?[/]", defaultValue: true))
                {
                    await SaveAsync(ct);
                    AnsiConsole.MarkupLine("[green]✓ Saved.[/]");
                }
            }
            return false;
        }
        if (input.Equals("Y", StringComparison.OrdinalIgnoreCase))
        {
            PromptYearFilter();
            RebuildGroups();
            return true;
        }
        if (int.TryParse(input, out var catNum) && catNum >= 1 && catNum <= _groups.Count)
        {
            await ShowCategoryAsync(_groups[catNum - 1], ct);
            return true;
        }

        return true;
    }
}

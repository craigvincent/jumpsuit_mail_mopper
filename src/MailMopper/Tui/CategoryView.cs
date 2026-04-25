using System.Globalization;
using MailMopper.Models;
using Spectre.Console;

namespace MailMopper.Tui;

/// <summary>
/// Category drill-down view — lists senders within a category with bulk actions.
/// </summary>
public partial class ReviewApp
{
    private async Task ShowCategoryAsync(ClassificationGroup group, CancellationToken ct)
    {
        int lastSelectedIndex = -1;
        int currentPage = 0;
        bool hidingDecided = true;

        var inCategory = true;
        while (inCategory)
        {
            AnsiConsole.Clear();

            var allSenders = BuildSenderList(group);

            var filteredSenders = hidingDecided
                ? allSenders.Where(s => s.Decision == ReviewDecision.Pending).ToList()
                : allSenders;

            int totalPending = allSenders.Count(s => s.Decision == ReviewDecision.Pending);
            int totalDecided = allSenders.Count - totalPending;
            int totalFiltered = filteredSenders.Count;
            int totalPages = Math.Max(1, (int)Math.Ceiling((double)totalFiltered / PageSize));

            if (currentPage >= totalPages)
                currentPage = totalPages - 1;
            if (currentPage < 0)
                currentPage = 0;

            int startIdx = currentPage * PageSize;
            int endIdx = Math.Min(startIdx + PageSize, totalFiltered);
            var pageSenders = filteredSenders.Skip(startIdx).Take(PageSize).ToList();

            var yearInfo = _yearFilter.HasValue ? $" [cyan]({_yearFilter.Value})[/]" : "";
            AnsiConsole.Write(new Rule($"[bold blue]{group.Category}[/]{yearInfo} — {group.Classifications.Count:N0} emails, {allSenders.Count} senders").LeftJustified());

            var pct = allSenders.Count > 0 ? totalDecided * 100 / allSenders.Count : 100;
            var bar = new string('█', pct / 5) + new string('░', 20 - pct / 5);
            AnsiConsole.MarkupLine($"  [green]{bar}[/] {totalDecided}/{allSenders.Count} senders reviewed ({pct}%)");

            if (totalFiltered == 0)
            {
                AnsiConsole.WriteLine();
                if (hidingDecided && totalDecided > 0)
                    AnsiConsole.MarkupLine("  [green]All senders in this category have been reviewed![/] Press [blue]H[/] to show all, or [yellow]B[/] to go back.");
                else
                    AnsiConsole.MarkupLine("  [dim]No senders to display.[/]");
                AnsiConsole.WriteLine();
            }
            else
            {
                var filterLabel = hidingDecided ? "[green]pending only[/]" : "[dim]all senders[/]";
                AnsiConsole.MarkupLine($"  Showing {startIdx + 1}–{endIdx} of {totalFiltered} senders ({filterLabel})");
                if (totalPages > 1)
                    AnsiConsole.MarkupLine($"  Page [bold]{currentPage + 1}[/] of {totalPages}");
                AnsiConsole.WriteLine();

                var senderTable = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn(new TableColumn("[bold]#[/]").RightAligned())
                    .AddColumn("[bold]Sender[/]")
                    .AddColumn(new TableColumn("[bold]Emails[/]").RightAligned())
                    .AddColumn("[bold]Domain[/]")
                    .AddColumn("[bold]Status[/]");

                for (int i = 0; i < pageSenders.Count; i++)
                {
                    var s = pageSenders[i];
                    var status = FormatDecision(s.Decision);
                    var from = s.From.Length > 40 ? s.From[..37] + "..." : s.From;
                    var displayNum = startIdx + i + 1;
                    var marker = (startIdx + i) == lastSelectedIndex ? "[bold cyan]›[/]" : " ";
                    senderTable.AddRow(
                        $"{marker}{displayNum}",
                        Markup.Escape(from),
                        s.Classifications.Count.ToString("N0", CultureInfo.InvariantCulture),
                        Markup.Escape(s.Domain),
                        status);
                }

                AnsiConsole.Write(senderTable);
                AnsiConsole.WriteLine();
            }

            var navHints = new List<string> { "[blue]#[/]=view" };
            navHints.Add("[red]T#[/]=trash");
            navHints.Add("[green]K#[/]=keep");
            if (currentPage > 0)
                navHints.Add("[blue]P[/]=prev");
            if (currentPage < totalPages - 1)
                navHints.Add("[blue]N[/]=next");
            navHints.Add(hidingDecided ? "[blue]H[/]=show all" : "[blue]H[/]=pending only");
            navHints.Add("[cyan]Y[/]=year");
            navHints.Add("[red]TA[/]=trash all");
            navHints.Add("[green]KA[/]=keep all");
            navHints.Add("[yellow]B[/]=back");

            AnsiConsole.MarkupLine($"  [dim]{string.Join(" │ ", navHints)}[/]");

            var defaultAction = lastSelectedIndex >= 0 && lastSelectedIndex >= startIdx && lastSelectedIndex < endIdx
                ? (lastSelectedIndex + 1).ToString(CultureInfo.InvariantCulture) : "";
            var defaultHint = defaultAction != "" ? $" (Enter=#{defaultAction})" : "";
            var input = ReadCommand($"[blue]Choice{defaultHint}: [/]", "BHNPY", defaultAction);

            var trimmed = input.Trim();

            if (trimmed.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                inCategory = false;
            }
            else if (trimmed.Equals("N", StringComparison.OrdinalIgnoreCase))
            {
                if (currentPage < totalPages - 1)
                    currentPage++;
            }
            else if (trimmed.Equals("P", StringComparison.OrdinalIgnoreCase))
            {
                if (currentPage > 0)
                    currentPage--;
            }
            else if (trimmed.Equals("H", StringComparison.OrdinalIgnoreCase))
            {
                hidingDecided = !hidingDecided;
                currentPage = 0;
                lastSelectedIndex = -1;
            }
            else if (trimmed.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                PromptYearFilter();
                RebuildGroups();

                var updatedGroup = _groups.FirstOrDefault(g => g.Category == group.Category);
                if (updatedGroup == null || updatedGroup.Classifications.Count == 0)
                {
                    inCategory = false;
                }
                else
                {
                    group = updatedGroup;
                    currentPage = 0;
                    lastSelectedIndex = -1;
                }
            }
            else if (trimmed.Equals("TA", StringComparison.OrdinalIgnoreCase))
            {
                var pending = group.Classifications.Where(c => c.ReviewDecision == ReviewDecision.Pending).ToList();
                if (pending.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No pending emails to trash.[/]");
                    await Task.Delay(500, ct);
                }
                else
                {
                    foreach (var c in pending)
                        c.ReviewDecision = ReviewDecision.ApproveTrash;
                    MarkDirty(pending.Count);
                    await AutoSaveIfNeeded(ct);
                    AnsiConsole.MarkupLine($"[red]✗ Marked {pending.Count:N0} pending emails for trash.[/]");
                    await Task.Delay(400, ct);
                }
            }
            else if (trimmed.Equals("KA", StringComparison.OrdinalIgnoreCase))
            {
                var pending = group.Classifications.Where(c => c.ReviewDecision == ReviewDecision.Pending).ToList();
                if (pending.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No pending emails to keep.[/]");
                    await Task.Delay(500, ct);
                }
                else
                {
                    foreach (var c in pending)
                        c.ReviewDecision = ReviewDecision.Keep;
                    MarkDirty(pending.Count);
                    await AutoSaveIfNeeded(ct);
                    AnsiConsole.MarkupLine($"[green]✓ Marked {pending.Count:N0} pending emails to keep.[/]");
                    await Task.Delay(400, ct);
                }
            }
            else if (trimmed.Length >= 2
                     && (trimmed[0] == 'T' || trimmed[0] == 't' || trimmed[0] == 'K' || trimmed[0] == 'k')
                     && int.TryParse(trimmed[1..], out var quickNum)
                     && quickNum >= 1 && quickNum <= totalFiltered)
            {
                var target = filteredSenders[quickNum - 1];
                var decision = char.ToUpper(trimmed[0], CultureInfo.InvariantCulture) == 'T'
                    ? ReviewDecision.ApproveTrash
                    : ReviewDecision.Keep;

                foreach (var c in target.Classifications)
                    c.ReviewDecision = decision;
                target.Decision = decision;
                MarkDirty(target.Classifications.Count);
                await AutoSaveIfNeeded(ct);

                var label = decision == ReviewDecision.ApproveTrash ? "[red]trash[/]" : "[green]keep[/]";
                AnsiConsole.MarkupLine($"  → {Markup.Escape(target.From)}: {label} ({target.Classifications.Count} emails)");

                lastSelectedIndex = FindNextPendingIndex(filteredSenders, quickNum - 1);
                currentPage = lastSelectedIndex >= 0 ? lastSelectedIndex / PageSize : currentPage;
            }
            else if (int.TryParse(trimmed, out var num) && num >= 1 && num <= totalFiltered)
            {
                lastSelectedIndex = num - 1;
                var selectedSender = filteredSenders[lastSelectedIndex];
                var result = await ShowSenderAsync(selectedSender, ct);

                if (result != SenderAction.Back)
                {
                    await AutoSaveIfNeeded(ct);

                    lastSelectedIndex = FindNextPendingIndex(filteredSenders, lastSelectedIndex);
                    currentPage = lastSelectedIndex >= 0 ? lastSelectedIndex / PageSize : currentPage;
                }
                else
                {
                    currentPage = lastSelectedIndex / PageSize;
                }
            }
        }

        var updatedGrp = _groups.FirstOrDefault(g => g.Category == group.Category);
        if (updatedGrp != null)
        {
            updatedGrp.Decision = updatedGrp.Classifications.All(c => c.ReviewDecision == ReviewDecision.ApproveTrash) ? ReviewDecision.ApproveTrash
                : updatedGrp.Classifications.All(c => c.ReviewDecision == ReviewDecision.Keep) ? ReviewDecision.Keep
                : updatedGrp.Classifications.All(c => c.ReviewDecision == ReviewDecision.Whitelisted) ? ReviewDecision.Whitelisted
                : ReviewDecision.Pending;
        }
    }
}
